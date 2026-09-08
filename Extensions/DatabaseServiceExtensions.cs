using Hub.Interfaces;
using Hub.Models;
using Hub.Models.Master;
using Hub.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Hub.Extensions;

public static class DatabaseServiceExtensions
{
    public static IServiceCollection AddDatabaseServices(this IServiceCollection services, string connectionString)
    {
        services.AddScoped<IConnectionStringManager, ConnectionStringManager>();

        services.AddHealthChecks().AddSqlServer(connectionString);

        services.AddDbContext<MasterContext>(x =>
        {
            x.UseSqlServer(connectionString, o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
#if DEBUG
            x.UseLoggerFactory(LoggerFactory.Create(c => c.AddDebug()));
#endif
        });

        services.AddScoped(serviceProvider =>
        {
            var optionsBuilder = new DbContextOptionsBuilder<DespatchContext>();
            var connectionStringManager = serviceProvider.GetRequiredService<IConnectionStringManager>();
            return new DynamicDespatchDbContext(optionsBuilder.Options, connectionStringManager);
        });

        services.AddScoped<IDespatchRepository, Repository>();
        services.AddScoped<IAuthenticationRepository, AuthenticationRepository>();

        // Singletons, both. The signing key is parsed once and held, because IdentityModel caches a
        // signature provider against the SecurityKey instance it was given - a key created per
        // request would be disposed while that cache still pointed at it.
        //
        return services;
    }
}