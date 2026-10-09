namespace MedSmarter.Modules.Identity.Contracts;

/// <summary>An active care relationship as seen by authorization: "this provider (a person or an organization) may be a care provider for this patient".</summary>
/// <param name="Kind">Treating or Dispensing: decides which consent purposes can open the data.</param>
public sealed record ActiveCareLink(Guid PatientUserId, Guid? ProviderUserId, Guid? ProviderOrganizationId, string Kind);

/// <summary>
/// Provider of dynamic care relationships (started with the consent of both sides, endable at any time). The authorizer merges every
/// registered source with the static catalog relationships, so a relationship that ended stops granting access on the next request.
/// </summary>
public interface ICareRelationshipSource
{
    Task<IReadOnlyList<ActiveCareLink>> ActiveForPatientAsync(Guid patientUserId, CancellationToken ct = default);

    Task<IReadOnlyList<ActiveCareLink>> ActiveForProviderAsync(Guid providerUserId, IReadOnlyCollection<Guid> organizationIds, CancellationToken ct = default);
}

public sealed record DemoPatientAccount(string AccountId, Guid UserId, string DisplayNameEn, string DisplayNameFa);

/// <summary>Development-only view of the fictional demo patients and their catalog relationships, used to seed demo records in other modules.</summary>
public interface IDemoCareData
{
    IReadOnlyList<DemoPatientAccount> PatientAccounts();

    IReadOnlyList<ActiveCareLink> CareLinks();
}
