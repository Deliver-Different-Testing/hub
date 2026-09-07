using System.Security.AccessControl;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Hub;
using Hub.Extensions;
using Hub.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.StaticFiles;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddHealthChecks();
builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
Log.Logger = new LoggerConfiguration().ReadFrom.Configuration(builder.Configuration).WriteTo.Console().CreateLogger();

builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();

var connectionString = Environment.GetEnvironmentVariable("MasterSQLConnection") ?? string.Empty;
if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException(
        "Could not find a connection string named 'MasterSQLConnection'.");
}

var domain = Environment.GetEnvironmentVariable("Domain") ?? string.Empty;
if (string.IsNullOrEmpty(domain) && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "Could not find a env var string named 'Domain'.");
}

var redisConfig = Environment.GetEnvironmentVariable("RedisConfig");
if (string.IsNullOrEmpty(redisConfig))
{
    throw new InvalidOperationException(
        "Could not find a Redis Env Var named 'RedisConfig'.");
}

builder.Services
    .AddDatabaseServices(connectionString)
    .AddAwsServices(builder.Configuration)
    .AddRedisServices(redisConfig)
    .AddAppServices();

if (builder.Environment.IsDevelopment())
{
    var keyDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DeliverDifferent", "DataProtection-Keys");

    // Ensure directory exists with proper permissions
    if (!Directory.Exists(keyDirectory))
    {
        var dirInfo = Directory.CreateDirectory(keyDirectory);

        if (OperatingSystem.IsWindows())
        {
            // Get current user's identity
            var currentUser = System.Security.Principal.WindowsIdentity.GetCurrent();
            const FileSystemRights fileSystemRights = FileSystemRights.FullControl;
            const InheritanceFlags inheritanceFlags = InheritanceFlags.ContainerInherit |
                                                      InheritanceFlags.ObjectInherit;
            const PropagationFlags propagationFlags = PropagationFlags.None;
            const AccessControlType accessControlType = AccessControlType.Allow;

            var accessRule = new FileSystemAccessRule(
                currentUser.Name,
                fileSystemRights,
                inheritanceFlags,
                propagationFlags,
                accessControlType);

            var security = dirInfo.GetAccessControl();
            security.AddAccessRule(accessRule);
            dirInfo.SetAccessControl(security);
        }
    }

    if (OperatingSystem.IsWindows())
    {
        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory))
            .SetApplicationName("DeliverDifferent")
            .ProtectKeysWithDpapi();
    }

    Log.Information("DataProtection configured to use directory: {KeyDirectory}", keyDirectory);
}
else
{
    builder.Services.AddDataProtection().PersistKeysToAWSSystemsManager("/Hub/DataProtection")
        .SetApplicationName("DeliverDifferent");
}

builder.Services.AddAuthentication("Identity.Application")
    .AddCookie("Identity.Application", options =>
    {
        options.Cookie.Name = ".AspNet.SharedCookie";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(20);
        options.SlidingExpiration = true;
        options.AccessDeniedPath = "/Forbidden/";
        options.LoginPath = "/Account/Login";
        options.Cookie.HttpOnly = true;
        options.Cookie.Domain = domain;
    });

builder.Services.AddSession(options =>
{
    options.Cookie.Name = "hub_session";
    options.IdleTimeout = TimeSpan.FromMinutes(60 * 24);
});

// Rate limiting.
//
// Eight [EnableRateLimiting] attributes were already on controllers across this application, naming
// "api" and "auth" - and none of them did anything, because the limiter was never registered. This
// registers it, which means those attributes start being honoured the moment a policy of that name
// exists.
//
// So "api" and "auth" are defined here as deliberate no-ops. Turning on real limits for sign-in,
// admin users, the partner directory and tenant branding all at once is a live behaviour change
// across endpoints that have never been limited, and it is not this change's to make - it needs
// numbers somebody has chosen. Naming them keeps the attributes from throwing "No policy found"
// while leaving exactly one seam to fill in when those numbers exist.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("api", _ => RateLimitPartition.GetNoLimiter("all"));
    options.AddPolicy("auth", _ => RateLimitPartition.GetNoLimiter("all"));

    // Merchant sign-in answers a guess at a password, which is a far softer target than an
    // eighty-bit pairing code. This limit is load-bearing rather than belt-and-braces.
    //
    // Partitioned by shop, and that is the point: every request arrives from the front door's egress
    // address, so an IP partition would put every merchant on every tenant in one bucket and let a
    // single merchant's typos lock the estate out. The shop travels in the query string precisely so
    // a partitioner - which sees the HttpContext and never a deserialised body - can reach it.
    //
    // A global cap sits behind it, below, because per-shop alone bounds nothing against a spray
    // across thousands of shop domains.
    options.AddPolicy(RateLimitPolicies.ShopifyMerchantSignIn, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            ShopPartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    // The other half of that: one ceiling across every shop at once. It has to live on the global
    // limiter rather than in the policy above, because a policy resolves to exactly one partition
    // and cannot bound two things.
    //
    // Every other path returns NoLimiter, so this adds a bound to the Shopify sign-in surface and
    // changes nothing anywhere else - which matters, because the global limiter is the one piece of
    // this that every request in the application passes through.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        context.Request.Path.StartsWithSegments("/api/shopify")
            ? RateLimitPartition.GetFixedWindowLimiter(
                "all-shopify-signins",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 300,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0
                })
            : RateLimitPartition.GetNoLimiter<string>("unlimited"));
});

// A request with no shop is refused by the endpoint anyway; keying those by caller stops them
// sharing one partition and starving each other on the way to that refusal.
static string ShopPartitionKey(HttpContext context)
{
    var shop = context.Request.Query["shop"].ToString().Trim().ToLowerInvariant();

    return shop.Length > 0 ? $"shop:{shop}" : $"ip:{context.Connection.RemoteIpAddress}";
}

var app = builder.Build();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/healthz");
app.MapGet("/diagnostics", async (AuthDiagnostics diagnostics) =>
    await diagnostics.RunDiagnosticsAsync());

// Configure the HTTP request pipeline.
var provider = new FileExtensionContentTypeProvider { Mappings = { [".tpl"] = "text/plain" } };

app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = provider,
    OnPrepareResponse = x =>
    {
        var path = x.Context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/dist/") || path.StartsWith("/images/"))
        {
            x.Context.Response.Headers.Append("Cache-Control", "public, max-age=86400");
        }
        else
        {
            x.Context.Response.Headers.Append("Cache-Control", "no-cache, no-store");
            x.Context.Response.Headers.Append("Pragma", "no-cache");
            x.Context.Response.Headers.Append("Expires", "0");
        }
    }
});

app.UseSession();
app.UseCookiePolicy();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");


app.Run();
