using System.Diagnostics.CodeAnalysis;
using StackExchange.Redis;

namespace Hub.Extensions;

[ExcludeFromCodeCoverage]
public static class RedisServiceExtensions
{
    public static IServiceCollection AddRedisServices(this IServiceCollection services, string redisConfig)
    {
        var redisConfigurationOptions = ConfigurationOptions.Parse(redisConfig);

        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(redisConfigurationOptions));

        services.AddStackExchangeRedisCache(redisCacheConfig =>
        {
            redisCacheConfig.ConfigurationOptions = redisConfigurationOptions;
        });

        return services;
    }
}
