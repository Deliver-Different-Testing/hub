using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System;
using UrgentHub.Models;
using UrgentHub.Repositories;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddHealthChecks();
builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
builder.Services.AddControllersWithViews();
// Add services to the container.
builder.Services.AddHttpClient();
builder.Services.AddScoped<Repository, Repository>();



var connectionString = Environment.GetEnvironmentVariable("SQLConnection") ?? "";
if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException(
        "Could not find a connection string named 'SQLConnection'.");
}
builder.Services.AddHealthChecks().AddSqlServer(connectionString);
builder.Services.AddDbContext<DespatchContext>(x =>
{
    x.UseSqlServer(connectionString);
#if DEBUG
    x.UseLoggerFactory(LoggerFactory.Create(c => c.AddDebug()));
#endif
});

builder.Services.AddDataProtection().PersistKeysToAWSSystemsManager("/Hub/DataProtection");

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.ExpireTimeSpan = TimeSpan.FromMinutes(20);
        options.SlidingExpiration = true;
        options.AccessDeniedPath = "/Forbidden/";
        options.LoginPath = "/Account/Login";
        options.Cookie.HttpOnly = true;
        options.Cookie.Domain = "*.deliverdifferent.com";

    });

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
app.UseAuthentication();
app.UseHttpsRedirection();
app.UseCookiePolicy();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");


app.Run();