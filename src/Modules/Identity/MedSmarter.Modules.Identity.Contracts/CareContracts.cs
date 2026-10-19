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

    /// <summary>
    /// The same links as <see cref="ActiveForPatientAsync"/> for each of the patients, in one list (each link names its patient). The default asks one patient at a time;
    /// an implementation may read them together as long as exactly the same links come back.
    /// </summary>
    async Task<IReadOnlyList<ActiveCareLink>> ActiveForPatientsAsync(IReadOnlyCollection<Guid> patientUserIds, CancellationToken ct = default)
    {
        var all = new List<ActiveCareLink>();
        foreach (var id in patientUserIds)
        {
            all.AddRange(await ActiveForPatientAsync(id, ct));
        }

        return all;
    }

    Task<IReadOnlyList<ActiveCareLink>> ActiveForProviderAsync(Guid providerUserId, IReadOnlyCollection<Guid> organizationIds, CancellationToken ct = default);
}

public sealed record DemoPatientAccount(string AccountId, Guid UserId, string DisplayNameEn, string DisplayNameFa);

/// <summary>Development-only view of the fictional demo patients and their catalog relationships, used to seed demo records in other modules.</summary>
public interface IDemoCareData
{
    IReadOnlyList<DemoPatientAccount> PatientAccounts();

    IReadOnlyList<ActiveCareLink> CareLinks();
}
