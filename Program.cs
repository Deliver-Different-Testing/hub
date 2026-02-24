using Amazon;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Amazon.S3;
using Hub;
using Hub.Models;
using Hub.Models.Master;
using Hub.Repositories;
using Hub.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using StackExchange.Redis;
using System;
using System.IO;
using System.Security.AccessControl;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddHealthChecks();
builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
Log.Logger = new LoggerConfiguration().ReadFrom.Configuration(builder.Configuration).WriteTo.Console().CreateLogger();

builder.Services.AddSingleton<IConnectionStringManager, ConnectionStringManager>();
builder.Services.AddControllersWithViews();
// Add services to the container.
builder.Services.AddHttpClient();

var connectionString = Environment.GetEnvironmentVariable("MasterSQLConnection") ?? "";
if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException(
        "Could not find a connection string named 'MasterSQLConnection'.");
}

builder.Services.AddHealthChecks().AddSqlServer(connectionString);
builder.Services.AddDbContext<MasterContext>(x =>
{
    x.UseSqlServer(connectionString);
#if DEBUG
    x.UseLoggerFactory(LoggerFactory.Create(c => c.AddDebug()));
#endif
});

// Register DespatchContext with a dummy connection string
builder.Services.AddDbContext<DespatchContext>((_, options) =>
{
    options.UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=dummy;Trusted_Connection=True;");
});
// Register DynamicDespatchDbContext
builder.Services.AddScoped<DynamicDespatchDbContext>((serviceProvider) =>
{
    var optionsBuilder = new DbContextOptionsBuilder<DespatchContext>();
    var connectionStringManager = serviceProvider.GetRequiredService<IConnectionStringManager>();

    // We're not setting the connection string here, it will be set in OnConfiguring
    return new DynamicDespatchDbContext(optionsBuilder.Options, connectionStringManager);
});


builder.Services.AddScoped<Repository, Repository>();
builder.Services.AddScoped<AuthenticationRepository, AuthenticationRepository>();
builder.Services.AddScoped<ITenantService, TenantService>();
builder.Services.AddSingleton<AuthDiagnostics>();

// Add memory cache for tenant logo service
builder.Services.AddMemoryCache();

// Add tenant logo service
builder.Services.AddScoped<ITenantLogoService, TenantLogoService>();

// Add tenant branding config service
builder.Services.AddScoped<ITenantBrandingConfigService, TenantBrandingConfigService>();

// AWS S3 Configuration
builder.Services.AddSingleton<IAmazonS3>(_ =>
{
    var awsOptions = builder.Configuration.GetAWSOptions();

    Log.Information("AWS Region from config: {Region}", awsOptions.Region?.SystemName ?? "null");

    var ssoCreds = LoadSsoCredentials("default");
    return new AmazonS3Client(ssoCreds, new AmazonS3Config
    {
        RegionEndpoint = awsOptions.Region ?? RegionEndpoint.APSoutheast2
    });
});

var domain = Environment.GetEnvironmentVariable("Domain") ?? "";
if (string.IsNullOrEmpty(domain))
{
    throw new InvalidOperationException(
        "Could not find a env var string named 'Domain'.");
}

// Configure Redis Based Distributed Session
var redisConfig = Environment.GetEnvironmentVariable("RedisConfig");
if (string.IsNullOrEmpty(redisConfig))
{
    throw new InvalidOperationException(
        "Could not find a Redis Env Var named 'RedisConfig'.");
}

var redisConfigurationOptions = ConfigurationOptions.Parse(redisConfig);
// Add Redis Connection Multiplexer
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(redisConfigurationOptions));


builder.Services.AddStackExchangeRedisCache(redisCacheConfig =>
{
    redisCacheConfig.ConfigurationOptions = redisConfigurationOptions;
});

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

var app = builder.Build();
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
        x.Context.Response.Headers.Append("Cache-Control", "no-cache, no-store");
        x.Context.Response.Headers.Append("Pragma", "no-cache");
        x.Context.Response.Headers.Append("Expires", "0");
    }
});

app.UseSession();

//app.UseHttpsRedirection();
app.UseCookiePolicy();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");


app.Run();

return;

//
// Method to get SSO credentials from the information in the shared config file.
static AWSCredentials LoadSsoCredentials(string profile)
{
    var chain = new CredentialProfileStoreChain();
    if (chain.TryGetAWSCredentials(profile, out var credentials)) return credentials;
    // If the SSO credentials are not found, use FallbackCredentialsFactory to get credentials
    credentials = FallbackCredentialsFactory.GetCredentials();
    return credentials ?? throw new Exception($"Failed to find the {profile} profile or any fallback credentials");
}