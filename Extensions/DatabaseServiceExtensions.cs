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
            x.UseSqlServer(connectionString);
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

        services.AddScoped<Repository, Repository>();
        services.AddScoped<AuthenticationRepository, AuthenticationRepository>();

        return services;
    }
}
