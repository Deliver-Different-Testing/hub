using System.Data;
using System.Text.RegularExpressions;
using Hub.Interfaces;
using Hub.Services;
using Microsoft.Data.SqlClient;
using Serilog;

namespace Hub.Features.Outbox;

public enum OutboxRegenerationStatus
{
    /// <summary>The message was rebuilt with a fresh reset key.</summary>
    Rebuilt,
    /// <summary>No claimed Regenerate row with that id in this tenant (wrong id, not being sent right now, or another tenant).</summary>
    NotFound,
    /// <summary>The row exists but cannot be rebuilt (contact gone, no login, no link). The worker fails it at once.</summary>
    Gone,
    /// <summary>This Hub cannot tell which tenant it serves, or cannot reach its database. The worker retries.</summary>
    Unavailable,
}

public sealed record OutboxRegenerationResult(
    OutboxRegenerationStatus Status, string? Subject = null, string? Body = null, bool IsHtml = false, string? Reason = null)
{
    public static OutboxRegenerationResult NotFound { get; } = new(OutboxRegenerationStatus.NotFound);
    public static OutboxRegenerationResult Gone(string reason) => new(OutboxRegenerationStatus.Gone, Reason: reason);
    public static OutboxRegenerationResult Unavailable(string reason) => new(OutboxRegenerationStatus.Unavailable, Reason: reason);
}

/// <summary>The Outbox row as dbo.OutboxMessage_GetForRegeneration returns it.</summary>
public sealed record OutboxRegenerationRow(long Id, string MessageType, string? RegenerationRef, int? ContactId,
    string? Subject, string? Body, bool IsHtml);

public sealed record OutboxContact(string? UserName, bool Active);

/// <summary>Tenant-DB reads for regeneration. Raw SqlClient: one proc and one projection.</summary>
public interface IOutboxRegenerationSource
{
    /// <summary>The row, only while the smppservice release worker has it claimed (Status 3) and it is in Regenerate mode.</summary>
    Task<OutboxRegenerationRow?> GetClaimedRowAsync(string tenantConnectionString, long id, CancellationToken ct);

    Task<OutboxContact?> GetContactAsync(string tenantConnectionString, int contactId, CancellationToken ct);
}

public interface IOutboxResetRegenerator
{
    Task<OutboxRegenerationResult> RegenerateAsync(long outboxId, CancellationToken ct);
}

/// <summary>
/// Rebuilds a held password reset or Hub invite for the smppservice release worker (pre-live outbound gating,
/// spec 5A.3). The link in those emails carries Master.User.ResetKey, which only Hub can issue, so the worker
/// asks Hub. A fresh key is issued, which also voids the key in the held copy, and the held body is returned
/// with its <c>code=</c> value replaced. The rest of the message is exactly what was held.
/// <para>
/// The tenant is this deployment's own (from TenantURL), never the caller's choice, and the row must be
/// claimed by the worker at that moment, so the endpoint cannot be used to mint a reset link for anyone else.
/// </para>
/// </summary>
public sealed partial class OutboxResetRegenerator(
    IAuthenticationRepository master,
    IOutboxRegenerationSource source,
    Func<string?>? tenantUrl = null,
    Func<string?>? sqlCredentials = null) : IOutboxResetRegenerator
{
    public static readonly IReadOnlySet<string> MessageTypes =
        new HashSet<string>(["PasswordReset", "HubInvite"], StringComparer.OrdinalIgnoreCase);

    public async Task<OutboxRegenerationResult> RegenerateAsync(long outboxId, CancellationToken ct)
    {
        var code = TenantCodeFrom((tenantUrl ?? (() => Environment.GetEnvironmentVariable("TenantURL")))());
        if (code is null) return OutboxRegenerationResult.Unavailable("This Hub cannot tell which tenant it serves (TenantURL)");

        var tenant = await master.GetTenantByCodeAsync(code);
        var credentials = (sqlCredentials ?? (() => Environment.GetEnvironmentVariable("SQLCredentials")))();
        if (tenant is null || string.IsNullOrEmpty(tenant.Dbconnection) || string.IsNullOrEmpty(credentials))
        {
            return OutboxRegenerationResult.Unavailable($"Tenant {code} or its database is not configured");
        }

        var connection = tenant.Dbconnection + credentials;
        var row = await source.GetClaimedRowAsync(connection, outboxId, ct);
        if (row is null) return OutboxRegenerationResult.NotFound;

        if (!MessageTypes.Contains(row.MessageType))
        {
            return OutboxRegenerationResult.Gone($"Hub rebuilds password resets and invites only, not {row.MessageType}");
        }

        var contactId = row.ContactId ?? (int.TryParse(row.RegenerationRef, out var parsed) ? parsed : (int?)null);
        if (contactId is null) return OutboxRegenerationResult.Gone("The held message does not say which contact it is for");

        var contact = await source.GetContactAsync(connection, contactId.Value, ct);
        if (contact is null || !contact.Active || string.IsNullOrWhiteSpace(contact.UserName))
        {
            return OutboxRegenerationResult.Gone("Contact no longer exists or is inactive");
        }

        if (string.IsNullOrEmpty(row.Body) || !ResetCode().IsMatch(row.Body))
        {
            return OutboxRegenerationResult.Gone("The held message has no reset link to refresh");
        }

        var user = await master.GetUserByEmailAsync(contact.UserName.Trim());
        if (user is null) return OutboxRegenerationResult.Gone("The contact no longer has a Hub login");

        if (user.CurrentTenantId != tenant.TenantId && !await master.IsUserAssociatedWithTenantAsync(user.UserId, tenant.TenantId))
        {
            return OutboxRegenerationResult.Gone("The contact's Hub login is not on this tenant");
        }

        var key = Guid.NewGuid().ToString();
        user.ResetKey = key;
        await master.SaveAsync();

        Log.Information("Outbox regeneration issued a fresh reset key for outbox row {OutboxId} (contact {ContactId}, tenant {TenantCode})",
            row.Id, contactId, code);

        return new OutboxRegenerationResult(OutboxRegenerationStatus.Rebuilt, row.Subject, ReplaceCode(row.Body, key), row.IsHtml);
    }

    /// <summary>Every <c>code=&lt;guid&gt;</c> in the body becomes the new key; nothing else changes.</summary>
    public static string ReplaceCode(string body, string key) => ResetCode().Replace(body, m => m.Groups[1].Value + key);

    /// <summary>TenantURL carries an app_name placeholder (https://app_name.urgent.deliverdifferent.com).</summary>
    public static string? TenantCodeFrom(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        return Uri.TryCreate(url.Replace("app_name", "hub", StringComparison.OrdinalIgnoreCase), UriKind.Absolute, out var uri)
            ? AccountService.ExtractTenantFromHost(uri.Host)
            : null;
    }

    [GeneratedRegex(@"([?&](?:amp;)?code=)[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}", RegexOptions.CultureInvariant)]
    private static partial Regex ResetCode();
}

public sealed class SqlOutboxRegenerationSource : IOutboxRegenerationSource
{
    public async Task<OutboxRegenerationRow?> GetClaimedRowAsync(string tenantConnectionString, long id, CancellationToken ct)
    {
        await using var conn = new SqlConnection(tenantConnectionString);
        await using var cmd = new SqlCommand("dbo.OutboxMessage_GetForRegeneration", conn)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 15,
        };
        cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = id;
        await conn.OpenAsync(ct);
        await using var r = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow, ct);
        if (!await r.ReadAsync(ct)) return null;

        return new OutboxRegenerationRow(
            r.GetInt64(r.GetOrdinal("Id")),
            r.GetString(r.GetOrdinal("MessageType")),
            Str(r, "RegenerationRef"),
            r.IsDBNull(r.GetOrdinal("ContactId")) ? null : r.GetInt32(r.GetOrdinal("ContactId")),
            Str(r, "Subject"),
            Str(r, "Body"),
            !r.IsDBNull(r.GetOrdinal("IsHtml")) && r.GetBoolean(r.GetOrdinal("IsHtml")));
    }

    public async Task<OutboxContact?> GetContactAsync(string tenantConnectionString, int contactId, CancellationToken ct)
    {
        await using var conn = new SqlConnection(tenantConnectionString);
        await using var cmd = new SqlCommand(
            "SELECT UserName, Active FROM dbo.tucClientContact WHERE ucctID = @Id", conn) { CommandTimeout = 15 };
        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = contactId;
        await conn.OpenAsync(ct);
        await using var r = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow, ct);
        if (!await r.ReadAsync(ct)) return null;
        return new OutboxContact(r.IsDBNull(0) ? null : r.GetString(0), !r.IsDBNull(1) && r.GetBoolean(1));
    }

    private static string? Str(SqlDataReader r, string column)
    {
        var i = r.GetOrdinal(column);
        return r.IsDBNull(i) ? null : r.GetString(i);
    }
}
