namespace MedSmarter.Modules.Identity;

/// <summary>Persistence ports. In-memory implementations serve development and tests; PostgreSQL replaces them later.</summary>
public interface IIdentityStore
{
    Task<User?> FindUserAsync(Guid id, CancellationToken ct);
    Task<User?> FindUserByIdentityAsync(string provider, string subject, CancellationToken ct);
    Task<UserProfile?> FindProfileAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyList<User>> AllUsersAsync(CancellationToken ct);
    Task AddUserAsync(User user, UserProfile profile, UserIdentityLink? link, CancellationToken ct);
    Task<IReadOnlyList<UserRole>> RolesOfAsync(Guid userId, CancellationToken ct);
    Task AddRoleAsync(UserRole role, CancellationToken ct);
    Task<IReadOnlyList<Guid>> ActiveOrganizationIdsAsync(Guid userId, CancellationToken ct);
    Task AddOrganizationAsync(Organization org, CancellationToken ct);
    Task AddMembershipAsync(OrganizationMembership membership, CancellationToken ct);
    Task<IReadOnlyList<CareRelationship>> RelationshipsForPatientAsync(Guid patientUserId, CancellationToken ct);
    Task AddRelationshipAsync(CareRelationship relationship, CancellationToken ct);
    Task<int> CountActiveHoldersAsync(string roleName, CancellationToken ct);
}

public interface ISessionStore
{
    Task<Device?> FindDeviceAsync(Guid id, CancellationToken ct);
    Task AddDeviceAsync(Device device, CancellationToken ct);
    Task<Session?> FindSessionAsync(Guid id, CancellationToken ct);
    Task AddSessionAsync(Session session, CancellationToken ct);
    Task<IReadOnlyList<Session>> SessionsOfAsync(Guid userId, CancellationToken ct);
    Task AddRefreshTokenAsync(RefreshToken token, CancellationToken ct);
    Task<RefreshToken?> FindRefreshTokenByHashAsync(string hash, CancellationToken ct);
    Task<IReadOnlyList<RefreshToken>> RefreshTokensOfAsync(Guid sessionId, CancellationToken ct);
}

public sealed class InMemoryIdentityStore : IIdentityStore, ISessionStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, User> _users = [];
    private readonly Dictionary<Guid, UserProfile> _profiles = [];
    private readonly List<UserIdentityLink> _links = [];
    private readonly List<UserRole> _roles = [];
    private readonly Dictionary<Guid, Organization> _orgs = [];
    private readonly List<OrganizationMembership> _memberships = [];
    private readonly List<CareRelationship> _relationships = [];
    private readonly Dictionary<Guid, Device> _devices = [];
    private readonly Dictionary<Guid, Session> _sessions = [];
    private readonly List<RefreshToken> _tokens = [];

    private Task<T> Run<T>(Func<T> f)
    {
        lock (_gate)
        {
            return Task.FromResult(f());
        }
    }

    public Task<User?> FindUserAsync(Guid id, CancellationToken ct) => Run(() => _users.GetValueOrDefault(id));

    public Task<User?> FindUserByIdentityAsync(string provider, string subject, CancellationToken ct) =>
        Run(() => _links.FirstOrDefault(l => l.Provider == provider && l.Subject == subject) is { } link ? _users.GetValueOrDefault(link.UserId) : null);

    public Task<UserProfile?> FindProfileAsync(Guid userId, CancellationToken ct) => Run(() => _profiles.GetValueOrDefault(userId));
    public Task<IReadOnlyList<User>> AllUsersAsync(CancellationToken ct) => Run<IReadOnlyList<User>>(() => [.. _users.Values]);

    public Task AddUserAsync(User user, UserProfile profile, UserIdentityLink? link, CancellationToken ct) => Run(() =>
    {
        _users[user.Id] = user;
        _profiles[user.Id] = profile;
        if (link is not null)
        {
            _links.Add(link);
        }

        return true;
    });

    public Task<IReadOnlyList<UserRole>> RolesOfAsync(Guid userId, CancellationToken ct) => Run<IReadOnlyList<UserRole>>(() => [.. _roles.Where(r => r.UserId == userId)]);
    public Task AddRoleAsync(UserRole role, CancellationToken ct) => Run(() => { _roles.Add(role); return true; });

    public Task<IReadOnlyList<Guid>> ActiveOrganizationIdsAsync(Guid userId, CancellationToken ct) =>
        Run<IReadOnlyList<Guid>>(() => [.. _memberships.Where(m => m.UserId == userId && m.IsActive && _orgs.TryGetValue(m.OrganizationId, out var o) && o.IsActive).Select(m => m.OrganizationId)]);

    public Task AddOrganizationAsync(Organization org, CancellationToken ct) => Run(() => { _orgs[org.Id] = org; return true; });
    public Task AddMembershipAsync(OrganizationMembership membership, CancellationToken ct) => Run(() => { _memberships.Add(membership); return true; });

    public Task<IReadOnlyList<CareRelationship>> RelationshipsForPatientAsync(Guid patientUserId, CancellationToken ct) =>
        Run<IReadOnlyList<CareRelationship>>(() => [.. _relationships.Where(r => r.PatientUserId == patientUserId)]);

    public Task AddRelationshipAsync(CareRelationship relationship, CancellationToken ct) => Run(() => { _relationships.Add(relationship); return true; });

    public Task<int> CountActiveHoldersAsync(string roleName, CancellationToken ct) =>
        Run(() => _roles.Where(r => r.RoleName == roleName && r.IsActive && _users.TryGetValue(r.UserId, out var u) && u.Status == UserStatusValue.Active).Select(r => r.UserId).Distinct().Count());

    // ---- sessions ----
    public Task<Device?> FindDeviceAsync(Guid id, CancellationToken ct) => Run(() => _devices.GetValueOrDefault(id));
    public Task AddDeviceAsync(Device device, CancellationToken ct) => Run(() => { _devices[device.Id] = device; return true; });
    public Task<Session?> FindSessionAsync(Guid id, CancellationToken ct) => Run(() => _sessions.GetValueOrDefault(id));
    public Task AddSessionAsync(Session session, CancellationToken ct) => Run(() => { _sessions[session.Id] = session; return true; });
    public Task<IReadOnlyList<Session>> SessionsOfAsync(Guid userId, CancellationToken ct) => Run<IReadOnlyList<Session>>(() => [.. _sessions.Values.Where(s => s.UserId == userId)]);
    public Task AddRefreshTokenAsync(RefreshToken token, CancellationToken ct) => Run(() => { _tokens.Add(token); return true; });
    public Task<RefreshToken?> FindRefreshTokenByHashAsync(string hash, CancellationToken ct) => Run(() => _tokens.FirstOrDefault(t => t.TokenHash == hash));
    public Task<IReadOnlyList<RefreshToken>> RefreshTokensOfAsync(Guid sessionId, CancellationToken ct) => Run<IReadOnlyList<RefreshToken>>(() => [.. _tokens.Where(t => t.SessionId == sessionId)]);
}
