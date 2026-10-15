using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;

namespace Hub.Features.AgentAccess.Oauth;

// Store for the OpenIddict OAuth server (applications, authorizations, tokens, scopes).
// Tables follow the Master DB convention (singular PascalCase, module prefix) as McpOAuth*;
// ids are Guids; datetime columns are UTC and end in Utc. Three tables: application,
// authorization, token (no scope table).
// Lives on the Master database (own login via AgentAccessOAuthSQLConnection, else
// MasterSQLConnection): the token endpoint is called by an
// AI app with no Hub login, so there is no tenant connection string to use. Tables are
// created by hand on the Master database from the DDL this model generates
// (Database.GenerateCreateScript), not by EF migrations (Hub has none).
public class AgentAccessOpenIddictDbContext(DbContextOptions<AgentAccessOpenIddictDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseOpenIddict<Guid>();

        modelBuilder.Entity<OpenIddictEntityFrameworkCoreApplication<Guid>>().ToTable("McpOAuthApplication");
        // No scope table: scopes are fixed (mcp, offline_access) and registered in the server options;
        // scope validation is switched off and enforced by our own allow-list.
        modelBuilder.Ignore<OpenIddictEntityFrameworkCoreScope<Guid>>();

        modelBuilder.Entity<OpenIddictEntityFrameworkCoreAuthorization<Guid>>(authorization =>
        {
            authorization.ToTable("McpOAuthAuthorization");
            authorization.Property(a => a.CreationDate).HasColumnName("CreationDateUtc");

            // The Master DB is shared by every tenant Hub, so each authorization records its tenant in the
            // Properties JSON (tenant_id). This persisted column makes per-tenant listing and revoking an
            // index seek instead of a table scan. Read-only: the database computes it.
            authorization.Property<int?>("TenantId")
                .HasComputedColumnSql("TRY_CAST(JSON_VALUE([Properties], '$.tenant_id') AS int)", stored: true);
            authorization.HasIndex("TenantId");
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
