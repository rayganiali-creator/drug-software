using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Consent.Contracts;
using MedSmarter.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace MedSmarter.Modules.Identity;

/// <summary>
/// Seeds the FICTIONAL demo identities, organizations, roles, care relationships and consents from the shared catalog.
/// Development/Testing only (Auth:Mode=DevelopmentMock). Contains no credentials.
/// </summary>
public sealed class DemoIdentitySeeder(AccessCatalog catalog, IIdentityStore identity, IDemoConsentSeeder consents, IClock clock, IOptions<AuthOptions> options) : IDemoIdentitySeeder
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (options.Value.Mode != AuthModes.DevelopmentMock)
        {
            throw new InvalidOperationException("Demo identities can only be seeded in DevelopmentMock mode.");
        }

        var now = clock.UtcNow;
        var f = catalog.File;
        var orgIds = f.Organizations.ToDictionary(o => o.Key, o => o.Id);
        var userIds = f.DemoAccounts.ToDictionary(a => a.Id, a => a.UserId);

        foreach (var o in f.Organizations)
        {
            await identity.AddOrganizationAsync(new Organization { Id = o.Id, Name = o.Name, Type = o.Type, CreatedAt = now }, ct);
        }

        foreach (var a in f.DemoAccounts)
        {
            var user = new User { Id = a.UserId, Status = Enum.Parse<UserStatusValue>(a.Status), CreatedAt = now, UpdatedAt = now, DisabledAt = a.Status == "Disabled" ? now : null };
            var profile = new UserProfile { UserId = a.UserId, DisplayName = a.DisplayName["en"], Email = a.Email, Locale = "en", UpdatedAt = now };
            var link = a.LoginEnabled ? new UserIdentityLink { Provider = MockAuthenticationProvider.ProviderId, Subject = a.Id, UserId = a.UserId, LinkedAt = now } : null;
            await identity.AddUserAsync(user, profile, link, ct);
            foreach (var r in a.Roles)
            {
                await identity.AddRoleAsync(new UserRole { UserId = a.UserId, RoleName = r.Role, OrganizationId = r.OrganizationId, AssignedAt = now }, ct);
            }

            foreach (var org in a.Organizations)
            {
                await identity.AddMembershipAsync(new OrganizationMembership { UserId = a.UserId, OrganizationId = org, JoinedAt = now }, ct);
            }
        }

        foreach (var r in f.CareRelationships)
        {
            await identity.AddRelationshipAsync(new CareRelationship
            {
                PatientUserId = userIds[r.PatientAccountId],
                ProviderUserId = r.ProviderAccountId is null ? null : userIds[r.ProviderAccountId],
                ProviderOrganizationId = r.ProviderOrganizationKey is null ? null : orgIds[r.ProviderOrganizationKey],
                Kind = Enum.Parse<CareRelationshipKind>(r.Kind),
                StartedAt = now.AddDays(-60),
            }, ct);
        }

        await consents.SeedAsync(f.Consents.Select(c => new ConsentDto(
            Guid.NewGuid(),
            userIds[c.SubjectAccountId],
            c.GranteeAccountId is null ? null : userIds[c.GranteeAccountId],
            c.GranteeOrganizationKey is null ? null : orgIds[c.GranteeOrganizationKey],
            c.Purpose,
            c.Scope,
            now.AddDays(-c.GrantedDaysAgo),
            now.AddDays(c.ExpiresInDays),
            null,
            c.ExpiresInDays < 0 ? ConsentStatus.Expired : ConsentStatus.Active,
            c.Version)), ct);
    }
}
