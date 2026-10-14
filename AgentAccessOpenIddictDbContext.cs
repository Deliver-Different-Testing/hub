using Microsoft.EntityFrameworkCore;

namespace Hub;

// Store for the OpenIddict OAuth server (applications, authorizations, tokens, scopes).
// Lives on the Master database (MasterSQLConnection): the token endpoint is called by an
// AI app with no Hub login, so there is no tenant connection string to use. Tables are
// created by docs/1.2-openiddict-master-schema.sql, not by EF migrations (Hub has none).
public class AgentAccessOpenIddictDbContext(DbContextOptions<AgentAccessOpenIddictDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseOpenIddict();
        base.OnModelCreating(modelBuilder);
    }
}
