using Hub.Interfaces;
using Hub.Models;
using Microsoft.EntityFrameworkCore;

namespace Hub.Features.AgentAccess.Capabilities;

/// <summary>The Access Control verbs (dbo.Feature, under agent-access) that say what an AI app may do for a user.</summary>
public static class AgentCapabilities
{
    public const string ParentKey = "agent-access";

    public const string Connect = "agent-connect";
    public const string ExportJobs = "agent-export-jobs";
    public const string ExportPricing = "agent-export-pricing";
    public const string SeeCosts = "agent-see-costs";
    public const string SeePod = "agent-see-pod";
    public const string SeeContactDetails = "agent-see-contact-details";
    public const string SeeNotes = "agent-see-notes";
    public const string ManageConnections = "agent-manage-connections";

    /// <summary>Sensitive data classes: plain verbs, independent of the tools.</summary>
    public static readonly IReadOnlyList<string> DataClasses = [SeeCosts, SeePod, SeeContactDetails, SeeNotes];

    /// <summary>Every capability: the fixed verbs, one per tool, the data classes and connection management.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Connect, ExportJobs, ExportPricing,
        .. AgentToolCatalogue.Tools.Select(t => t.Verb),
        .. DataClasses, ManageConnections
    ];

    // An export may never show more than the user can open in the app it reads from.
    internal static readonly IReadOnlyDictionary<string, string> ExportTiles = new Dictionary<string, string>
    {
        [ExportJobs] = "hub-tile-despatchweb",
        [ExportPricing] = "hub-tile-configurator"
    };
}

/// <param name="ContactId">tucClientContact.ucctID.</param>
/// <param name="ClientId">The contact's client, for per-client overrides and the DF Admin check.</param>
/// <param name="Internal">tucClient.ucclInternal: tenant staff on a Customer client resolve as the tenant for gate 1.</param>
/// <param name="CountryCode">The tenant's market, for gate 1's country scope.</param>
public sealed record AgentCapabilitySubject(int ContactId, int? ClientId, bool Internal, string? CountryCode);

/// <param name="Verb">The tool's own verb is ticked for the role.</param>
/// <param name="Screen">The hub tile and every screen key the tool needs are granted.</param>
/// <param name="Reason">Plain words for why the tool is blocked; null when nothing blocks it.</param>
public sealed record AgentToolDecision(bool Verb, bool Screen, string? Reason);

/// <param name="Capabilities">Granted keys only: a tool verb is listed only when its verb, tile and screens all pass.</param>
/// <param name="Tools">A decision for every tool verb, granted or not.</param>
public sealed record AgentCapabilityDecision(IReadOnlySet<string> Capabilities, IReadOnlyDictionary<string, AgentToolDecision> Tools)
{
    public static AgentCapabilityDecision Empty { get; } =
        new(new HashSet<string>(StringComparer.Ordinal), new Dictionary<string, AgentToolDecision>());
}

public interface IAgentCapabilityResolver
{
    /// <summary>The capabilities and per-tool decisions this contact's AI apps hold now. No agent-connect means no capabilities.</summary>
    Task<AgentCapabilityDecision> ResolveDetailedAsync(AgentCapabilitySubject subject, CancellationToken cancellationToken = default);

    /// <summary>The capabilities this contact's AI apps hold now. Empty means none, including no agent-connect.</summary>
    async Task<IReadOnlySet<string>> ResolveAsync(AgentCapabilitySubject subject, CancellationToken cancellationToken = default) =>
        (await ResolveDetailedAsync(subject, cancellationToken)).Capabilities;

    /// <summary>The names of the active roles this contact holds, for the admin Connected AI apps list.</summary>
    Task<IReadOnlyList<string>> RoleNamesAsync(int contactId, CancellationToken cancellationToken = default);
}
// Every agent capability needs agent-connect. Screen keys are resolved like the configurator's RolePermissionResolver
// (per-client override beats the global row, union across stacked roles, an explicit None on an ancestor denies, else
// the nearest explicit level up the tree). The agent-* verbs are stricter and fail closed: a verb counts only when the
// role has an explicit row on that verb itself (global or the client's override) at level 1 or more, so a granted
// agent-access parent can never switch a verb on by cascade. An explicit None on an ancestor still denies. An export also
// needs the hub tile of the app it reads, decided by Hub's own gates 1 and 2, so it never shows more than the launcher.
//
// Hub deliberately differs from the configurator for duplicate rows on the same role, key and scope (the table has no
// unique constraint): configurator takes the first row it meets, Hub takes the lowest level, so a stray duplicate can
// only ever deny, never grant. Instances are scoped to a request: the feature tree, role ids and permission rows are
// read once and reused for every holder resolved in it (the admin connections list resolves hundreds).
public sealed class AgentCapabilityResolver(
    DynamicDespatchDbContext context,
    IFeatureResolver featureResolver,
    ITileAccessResolver tileAccessResolver) : IAgentCapabilityResolver
{
    private const int DfAdminClientType = 5;

    private Dictionary<string, string>? _parents;
    private readonly Dictionary<int, List<int>> _roleIds = [];
    private readonly Dictionary<(int Role, int? Client), List<PermissionRow>> _rows = [];

    private sealed record PermissionRow(string Key, bool IsOverride, byte Level);

    private static readonly IReadOnlyDictionary<string, string> TileNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["hub-tile-despatchweb"] = "Despatch Web",
        ["hub-tile-configurator"] = "Configurator"
    };

    public async Task<AgentCapabilityDecision> ResolveDetailedAsync(AgentCapabilitySubject subject, CancellationToken cancellationToken = default)
    {
        var clientTypeId = subject.ClientId is > 0
            ? await context.TucClients.AsNoTracking()
                .Where(c => c.UcclId == subject.ClientId.Value)
                .Select(c => (int?)c.ClientTypeId)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        if (clientTypeId == DfAdminClientType)
        {
            return new AgentCapabilityDecision(
                AgentCapabilities.All.ToHashSet(StringComparer.Ordinal),
                AgentToolCatalogue.Tools.ToDictionary(t => t.Verb, _ => new AgentToolDecision(true, true, null), StringComparer.Ordinal));
        }

        var roleIds = await RoleIdsAsync(subject.ContactId, cancellationToken);
        if (roleIds.Count == 0) return AgentCapabilityDecision.Empty;

        var tree = await LoadTreeAsync(roleIds, subject.ClientId, cancellationToken);

        var tiles = await GrantedTilesAsync(subject);

        // Client-type visibility needs no check here: despatchweb only gates Network Partners (type 3) and an agent pass
        // needs client type 4 or 5.
        var tools = new Dictionary<string, AgentToolDecision>(StringComparer.Ordinal);
        foreach (var tool in AgentToolCatalogue.Tools)
        {
            tools[tool.Verb] = Decide(tool, tree, tiles);
        }

        if (tree.VerbLevel(AgentCapabilities.Connect) < 1) return new AgentCapabilityDecision(new HashSet<string>(StringComparer.Ordinal), tools);

        var granted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var verb in AgentCapabilities.All)
        {
            if (tools.TryGetValue(verb, out var decision))
            {
                if (decision.Verb && decision.Screen) granted.Add(verb);
            }
            else if (tree.VerbLevel(verb) >= 1 && (!AgentCapabilities.ExportTiles.TryGetValue(verb, out var tile) || tiles.Contains(tile)))
            {
                granted.Add(verb);
            }
        }

        return new AgentCapabilityDecision(granted, tools);
    }

    // The screen-key rule: a key's resolved level must be 1 or more. A key with no explicit row on it or any ancestor
    // (the rollout state of most roles) follows the tile decision, the same fail-open the launcher uses; an explicit
    // None anywhere up the tree denies. The tool's own tile must also be granted.
    private static AgentToolDecision Decide(AgentToolDefinition tool, AccessTree tree, HashSet<string> tiles)
    {
        var verb = tree.VerbLevel(tool.Verb) >= 1;
        var tileOk = tiles.Contains(tool.HubTile);

        string? blockedBy = null;
        foreach (var key in tool.ScreenKeys)
        {
            var ok = tree.HasExplicit(key) ? tree.Level(key) >= 1 : tileOk;
            if (!ok)
            {
                blockedBy = key;
                break;
            }
        }

        var screen = tileOk && blockedBy is null;
        var reason = !tileOk
            ? $"{TileNames.GetValueOrDefault(tool.HubTile, tool.HubTile)} tile is off"
            : blockedBy is not null
                ? $"{blockedBy} is off for this role"
                : !verb ? $"{tool.Verb} is off for this role" : null;

        return new AgentToolDecision(verb, screen, reason);
    }

    private async Task<HashSet<string>> GrantedTilesAsync(AgentCapabilitySubject subject)
    {
        var enabled = await featureResolver.ResolveForClientAsync(subject.ClientId, subject.Internal, subject.CountryCode);
        var decisions = await tileAccessResolver.ResolveAsync(subject.ContactId, subject.ClientId, enabled);
        return decisions.Where(d => d.Granted).Select(d => d.TileKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<string>> RoleNamesAsync(int contactId, CancellationToken cancellationToken = default)
    {
        var roleIds = await RoleIdsAsync(contactId, cancellationToken);
        if (roleIds.Count == 0) return [];

        var names = await context.TblContactRoles.AsNoTracking()
            .Where(r => roleIds.Contains(r.ContactRoleId))
            .Select(r => r.Name)
            .ToListAsync(cancellationToken);
        return names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!).Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Stacked roles first, else the contact's single primary role; inactive roles count for nothing.
    private async Task<List<int>> RoleIdsAsync(int contactId, CancellationToken cancellationToken)
    {
        if (contactId <= 0) return [];
        if (_roleIds.TryGetValue(contactId, out var cached)) return cached;

        var ids = await LoadRoleIdsAsync(contactId, cancellationToken);
        _roleIds[contactId] = ids;
        return ids;
    }

    private async Task<List<int>> LoadRoleIdsAsync(int contactId, CancellationToken cancellationToken)
    {
        var stacked = await context.TblContactContactRoles.AsNoTracking()
            .Where(j => j.ClientContactId == contactId)
            .Join(context.TblContactRoles.AsNoTracking().Where(r => r.IsActive),
                j => j.ContactRoleId, r => r.ContactRoleId, (j, r) => r.ContactRoleId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (stacked.Count > 0) return stacked;

        var single = await context.TucClientContacts.AsNoTracking()
            .Where(c => c.UcctId == contactId)
            .Select(c => c.ContactRoleId)
            .FirstOrDefaultAsync(cancellationToken);
        if (single is not { } roleId || roleId <= 0) return [];

        return await context.TblContactRoles.AsNoTracking().AnyAsync(r => r.ContactRoleId == roleId && r.IsActive, cancellationToken)
            ? [roleId]
            : [];
    }

    private async Task<Dictionary<string, string>> ParentsAsync(CancellationToken cancellationToken) =>
        _parents ??= await context.Features.AsNoTracking()
            .Select(f => new { f.FeatureKey, f.ParentKey })
            .ToDictionaryAsync(f => f.FeatureKey, f => f.ParentKey, StringComparer.OrdinalIgnoreCase, cancellationToken);

    // Only the verbs, the screen keys and their ancestors decide anything here.
    private static HashSet<string> RelevantKeys(Dictionary<string, string> parents)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var start in AgentCapabilities.All.Concat(AgentToolCatalogue.Tools.SelectMany(t => t.ScreenKeys)))
        {
            for (var k = start; k is not null && keys.Add(k); k = parents.GetValueOrDefault(k)) { }
        }

        return keys;
    }

    private async Task<List<PermissionRow>> RowsAsync(int roleId, int? clientId, Dictionary<string, string> parents, CancellationToken cancellationToken)
    {
        if (_rows.TryGetValue((roleId, clientId), out var cached)) return cached;

        var keys = RelevantKeys(parents);
        var rows = await context.RolePermissions.AsNoTracking()
            .Where(rp => rp.ContactRoleId == roleId
                         && keys.Contains(rp.PermissionKey)
                         && (rp.ClientId == null || (clientId != null && rp.ClientId == clientId)))
            .Select(rp => new { rp.PermissionKey, rp.ClientId, rp.AccessLevel, rp.Allowed })
            .ToListAsync(cancellationToken);

        var list = rows
            .Select(r => new PermissionRow(r.PermissionKey.ToLowerInvariant(), r.ClientId != null, r.AccessLevel ?? (r.Allowed ? (byte)1 : (byte)0)))
            .ToList();
        _rows[(roleId, clientId)] = list;
        return list;
    }

    private async Task<AccessTree> LoadTreeAsync(List<int> roleIds, int? clientId, CancellationToken cancellationToken)
    {
        var parents = await ParentsAsync(cancellationToken);

        var levels = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        foreach (var roleId in roleIds)
        {
            // Per role: a client override beats the global row; duplicates at the same scope take the lowest level.
            var perKey = new Dictionary<string, (bool IsOverride, byte Level)>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in await RowsAsync(roleId, clientId, parents, cancellationToken))
            {
                if (!perKey.TryGetValue(row.Key, out var current)
                    || (row.IsOverride && !current.IsOverride)
                    || (row.IsOverride == current.IsOverride && row.Level < current.Level))
                {
                    perKey[row.Key] = (row.IsOverride, row.Level);
                }
            }

            // Union across stacked roles.
            foreach (var (key, (_, level)) in perKey)
            {
                if (!levels.TryGetValue(key, out var current) || level > current) levels[key] = level;
            }
        }

        return new AccessTree(parents, levels);
    }

    private sealed class AccessTree(Dictionary<string, string> parents, Dictionary<string, byte> levels)
    {
        // An explicit None on any ancestor denies; else the node's own explicit level; else the nearest explicit ancestor.
        public byte Level(string key)
        {
            if (AncestorDenies(key)) return 0;

            for (var k = key; k is not null; k = parents.GetValueOrDefault(k))
            {
                if (levels.TryGetValue(k, out var l)) return l;
            }

            return 0;
        }

        /// <summary>An agent-* verb: only its own explicit row counts (no cascade on from agent-access), and an explicit None on an ancestor still denies.</summary>
        public byte VerbLevel(string key) =>
            AncestorDenies(key) ? (byte)0 : levels.TryGetValue(key, out var l) ? l : (byte)0;

        private bool AncestorDenies(string key)
        {
            for (var a = parents.GetValueOrDefault(key); a is not null; a = parents.GetValueOrDefault(a))
            {
                if (levels.TryGetValue(a, out var l) && l == 0) return true;
            }

            return false;
        }

        /// <summary>True when the user's roles have a row on the key or any ancestor.</summary>
        public bool HasExplicit(string key)
        {
            for (var k = key; k is not null; k = parents.GetValueOrDefault(k))
            {
                if (levels.ContainsKey(k)) return true;
            }

            return false;
        }
    }
}
