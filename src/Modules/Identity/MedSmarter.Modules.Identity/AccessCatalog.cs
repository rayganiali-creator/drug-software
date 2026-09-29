using System.Reflection;
using System.Text.Json;

namespace MedSmarter.Modules.Identity;

public sealed record PermissionDefinition(string Name, string Kind, string Description, string? DataScope);
public sealed record CatalogRole(string Name, string Description, List<string> Permissions);
public sealed record CatalogOrganization(Guid Id, string Key, string Name, string Type);
public sealed record CatalogRoleAssignment(string Role, Guid? OrganizationId);
public sealed record CatalogAccount(
    string Id, Guid UserId, Dictionary<string, string> DisplayName, string? Email, List<CatalogRoleAssignment> Roles, List<Guid> Organizations,
    string Status, bool LoginEnabled, bool Primary, Dictionary<string, string> Description);
public sealed record CatalogSubject(string Key, string AccountId, Guid UserId);
public sealed record CatalogRelationship(string Kind, string PatientAccountId, string? ProviderAccountId, string? ProviderOrganizationKey);
public sealed record CatalogConsent(string SubjectAccountId, string? GranteeAccountId, string? GranteeOrganizationKey, string Purpose, List<string> Scope, int GrantedDaysAgo, int ExpiresInDays, string Version);

/// <summary>
/// The permission taxonomy and role→permission mapping, loaded from the shared catalog (embedded resource).
/// Everything that decides "can role X do Y" reads this one object; nothing else hard-codes permissions.
/// </summary>
public sealed class AccessCatalog
{
    private readonly Dictionary<string, PermissionDefinition> _permissions;
    private readonly Dictionary<string, CatalogRole> _roles;

    private AccessCatalog(CatalogFile file)
    {
        File = file;
        _permissions = file.Permissions.ToDictionary(p => p.Name, StringComparer.Ordinal);
        _roles = file.Roles.ToDictionary(r => r.Name, StringComparer.Ordinal);
    }

    public CatalogFile File { get; }
    public IReadOnlyCollection<PermissionDefinition> Permissions => _permissions.Values;
    public IReadOnlyCollection<CatalogRole> Roles => _roles.Values;

    public sealed record CatalogFile(
        int Version, List<string> ConsentPurposes, List<string> DataScopes, List<PermissionDefinition> Permissions, List<CatalogRole> Roles,
        List<CatalogOrganization> Organizations, List<CatalogAccount> DemoAccounts, List<CatalogSubject> Subjects,
        List<CatalogRelationship> CareRelationships, List<CatalogConsent> Consents);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static AccessCatalog LoadEmbedded()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("access-catalog.json")
            ?? throw new InvalidOperationException("access-catalog.json is not embedded");
        var file = JsonSerializer.Deserialize<CatalogFile>(stream, Json)
            ?? throw new InvalidOperationException("access-catalog.json is invalid");
        return new AccessCatalog(file);
    }

    public bool IsKnownRole(string role) => _roles.ContainsKey(role);
    public bool IsKnownPermission(string permission) => _permissions.ContainsKey(permission);
    public string? KindOf(string permission) => _permissions.TryGetValue(permission, out var p) ? p.Kind : null;
    public string? DataScopeOf(string permission) => _permissions.TryGetValue(permission, out var p) ? p.DataScope : null;

    /// <summary>Union of the permissions of the given roles. Unknown roles contribute nothing (deny by default).</summary>
    public IReadOnlySet<string> PermissionsOf(IEnumerable<string> roles) =>
        roles.Where(_roles.ContainsKey).SelectMany(r => _roles[r].Permissions).ToHashSet(StringComparer.Ordinal);
}
