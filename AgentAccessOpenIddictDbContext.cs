using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;

namespace Hub;

// Store for the OpenIddict OAuth server (applications, authorizations, tokens, scopes).
// Tables follow the Master DB convention (singular PascalCase, module prefix) as McpOAuth*;
// ids are Guids; datetime columns are UTC and end in Utc.
// Lives on the Master database (MasterSQLConnection): the token endpoint is called by an
// AI app with no Hub login, so there is no tenant connection string to use. Tables are
// created by Database/1.2-openiddict-master-schema.sql, not by EF migrations (Hub has none).
public class AgentAccessOpenIddictDbContext(DbContextOptions<AgentAccessOpenIddictDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseOpenIddict<Guid>();

        modelBuilder.Entity<OpenIddictEntityFrameworkCoreApplication<Guid>>().ToTable("McpOAuthApplication");
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreScope<Guid>>().ToTable("McpOAuthScope");

        modelBuilder.Entity<OpenIddictEntityFrameworkCoreAuthorization<Guid>>(authorization =>
        {
            authorization.ToTable("McpOAuthAuthorization");
            authorization.Property(a => a.CreationDate).HasColumnName("CreationDateUtc");
        });

        modelBuilder.Entity<OpenIddictEntityFrameworkCoreToken<Guid>>(token =>
        {
            token.ToTable("McpOAuthToken");
            token.Property(t => t.CreationDate).HasColumnName("CreationDateUtc");
            token.Property(t => t.ExpirationDate).HasColumnName("ExpirationDateUtc");
            token.Property(t => t.RedemptionDate).HasColumnName("RedemptionDateUtc");
        });
        base.OnModelCreating(modelBuilder);
    }
}
