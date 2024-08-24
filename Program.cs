using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System;
using Microsoft.AspNetCore.DataProtection;
using UrgentHub.Models;
using UrgentHub.Repositories;
using System.Threading.Tasks;
using UrgentHub.Models.Master;
using UrgentHub;
using Serilog;
using UrgentHub.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddHealthChecks();
builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
Log.Logger =  new LoggerConfiguration().ReadFrom.Configuration(builder.Configuration).WriteTo.Console().CreateLogger();

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
builder.Services.AddDbContext<DespatchContext>((serviceProvider, options) =>
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

builder.Services.AddStackExchangeRedisCache(redisCacheConfig =>
{
    redisCacheConfig.ConfigurationOptions = redisConfigurationOptions;
});

builder.Services.AddDataProtection().PersistKeysToAWSSystemsManager("/Hub/DataProtection").SetApplicationName("DeliverDifferent");


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


builder.Services.AddSession(options => {
    options.Cookie.Name = "hub_session";
    options.IdleTimeout = TimeSpan.FromMinutes(60 * 24);
});

var app = builder.Build();
app.MapHealthChecks("/healthz");
// Configure the HTTP request pipeline.
var provider = new FileExtensionContentTypeProvider { Mappings = { [".tpl"] = "text/plain" } };

app.UseStaticFiles(new StaticFileOptions
{

    ContentTypeProvider = provider,
    OnPrepareResponse = x =>
    {
        x.Context.Response.Headers.Add("Cache-Control", "no-cache, no-store");
        x.Context.Response.Headers.Add("Pragma", "no-cache");
        x.Context.Response.Headers.Add("Expires", "0");
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