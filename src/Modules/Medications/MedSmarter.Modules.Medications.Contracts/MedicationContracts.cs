namespace MedSmarter.Modules.Medications.Contracts;

// ---------- vocabulary ----------

/// <summary>Whether a record is in use. Deactivating never deletes: history and references stay valid.</summary>
public enum LifecycleStatus
{
    Draft,
    Active,
    Inactive,
}

/// <summary>
/// How far the information has been checked. Only <see cref="Validated"/> may be presented as verified.
/// <see cref="Demo"/> is fictional test data and must never be shown as medical information.
/// </summary>
public enum ValidationStatus
{
    Demo,
    Unverified,
    NeedsValidation,
    Validated,
    Rejected,
}

public enum StatementKind
{
    Indication,
    Contraindication,
    Warning,
    Precaution,
    AdverseReaction,
    Administration,
    Storage,
}

public enum ReferenceKind
{
    DosageForm,
    Route,
    TherapeuticClass,
    DrugClass,
}

public enum IdentifierScheme
{
    NationalDrugCode,
    ExternalDrugId,
    ManufacturerCode,
    Gtin,
    AtcCode,
    ExternalSourceId,
}

public enum InteractionSeverity
{
    Unknown,
    Minor,
    Moderate,
    Major,
    Contraindicated,
}

public enum SourceType
{
    OfficialDatabase,
    Regulator,
    Publication,
    Internal,
    Demo,
}

public enum RevisionStatus
{
    Draft,
    InReview,
    Validated,
    Rejected,
}

public enum NameKind
{
    Generic,
    Brand,
    Ingredient,
    Synonym,
    Code,
}

// ---------- shared shapes ----------

public sealed record LocalizedText(string? En, string? Fa)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => string.IsNullOrWhiteSpace(En) && string.IsNullOrWhiteSpace(Fa);
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Limit, int Offset);

public sealed record ReferenceTermDto(Guid Id, ReferenceKind Kind, string Code, LocalizedText Name);

public sealed record KnowledgeSourceDto(
    Guid Id,
    string Name,
    string Publisher,
    SourceType Type,
    string? Url,
    string Version,
    string LicenseName,
    bool? RedistributionAllowed,
    string? UsageRestrictions,
    DateTimeOffset ReceivedAt,
    ValidationStatus Validation);

public sealed record KnowledgeRevisionDto(Guid Id, Guid SourceId, string Label, DateTimeOffset ReceivedAt, RevisionStatus Status, string? Notes);

public sealed record IngredientDto(Guid Id, LocalizedText Name, string? AtcCode, IReadOnlyList<string> Synonyms, LifecycleStatus Lifecycle, ValidationStatus Validation);

public sealed record MedicationIngredientDto(Guid IngredientId, LocalizedText Name, decimal? StrengthValue, string? StrengthUnit, string? PerUnit, int Order);

public sealed record BrandDto(Guid Id, LocalizedText Name, Guid? ManufacturerId);

public sealed record ManufacturerDto(Guid Id, LocalizedText Name, string? Country, string? ManufacturerCode);

public sealed record IdentifierDto(IdentifierScheme Scheme, string Value, Guid? SourceRevisionId);

public sealed record StatementDto(Guid Id, StatementKind Kind, LocalizedText Text, string? Severity, string? Frequency, string? Population, Guid RevisionId, Guid SourceId, ValidationStatus Validation);

public sealed record InteractionDto(
    Guid Id,
    Guid OtherIngredientId,
    LocalizedText OtherIngredientName,
    Guid ThisIngredientId,
    InteractionSeverity Severity,
    LocalizedText Mechanism,
    LocalizedText Management,
    Guid RevisionId,
    Guid SourceId,
    ValidationStatus Validation);

// ---------- read models ----------

/// <param name="MatchedOn">Which name matched the query (for highlighting); not a security-relevant field.</param>
public sealed record MedicationSummaryDto(
    Guid Id,
    LocalizedText Name,
    LocalizedText? BrandName,
    LocalizedText DosageForm,
    string StrengthSummary,
    IReadOnlyList<LocalizedText> Ingredients,
    LifecycleStatus Lifecycle,
    ValidationStatus Validation,
    bool IsDemo,
    string? MatchedOn,
    int Score);

public sealed record MedicationDetailDto(
    Guid Id,
    int Version,
    LocalizedText Name,
    BrandDto? Brand,
    ManufacturerDto? Manufacturer,
    LocalizedText DosageForm,
    IReadOnlyList<LocalizedText> Routes,
    string StrengthSummary,
    IReadOnlyList<MedicationIngredientDto> Ingredients,
    IReadOnlyList<ReferenceTermDto> Classifications,
    IReadOnlyList<string> Synonyms,
    IReadOnlyList<IdentifierDto> Identifiers,
    IReadOnlyList<StatementDto> Statements,
    /// <summary>Statement kinds for which NO information is recorded: shown as "information not available", never guessed.</summary>
    IReadOnlyList<StatementKind> MissingKinds,
    IReadOnlyList<InteractionDto> Interactions,
    IReadOnlyList<KnowledgeSourceDto> Sources,
    LifecycleStatus Lifecycle,
    ValidationStatus Validation,
    bool IsDemo,
    DateTimeOffset UpdatedAt,
    /// <summary>Human-readable notice that must accompany the data (e.g. "DEMO DATA - NOT FOR CLINICAL USE").</summary>
    string Notice);

/// <summary>
/// Structured, source-carrying view of one medication for retrieval by an AI assistant (RAG-ready). It is built only by
/// the Medications service; a language model never queries the database itself.
/// </summary>
public sealed record MedicationKnowledgeDocument(
    Guid MedicationId,
    int Version,
    IReadOnlyList<string> Names,
    IReadOnlyList<string> Synonyms,
    IReadOnlyList<string> Ingredients,
    string DosageForm,
    string Strength,
    IReadOnlyList<KnowledgeStatementView> Statements,
    IReadOnlyList<KnowledgeInteractionView> Interactions,
    IReadOnlyList<KnowledgeSourceRef> Sources,
    ValidationStatus Validation,
    bool IsDemo,
    DateTimeOffset UpdatedAt,
    string Notice);

public sealed record KnowledgeStatementView(StatementKind Kind, string Text, Guid SourceId, ValidationStatus Validation);

public sealed record KnowledgeInteractionView(string WithIngredient, InteractionSeverity Severity, string Summary, Guid SourceId, ValidationStatus Validation);

public sealed record KnowledgeSourceRef(Guid SourceId, string Name, string Version, string Publisher);

// ---------- commands ----------

public sealed record MedicationSearchQuery(string? Q, int Limit = 20, int Offset = 0, bool IncludeInactive = false);

public sealed record NewIdentifier(IdentifierScheme Scheme, string Value, Guid? SourceRevisionId);

public sealed record NewIngredientRef(Guid IngredientId, decimal? StrengthValue, string? StrengthUnit, string? PerUnit);

public sealed record NewStatement(StatementKind Kind, LocalizedText Text, string? Severity, string? Frequency, string? Population, Guid RevisionId);

public sealed record MedicationDraft(
    LocalizedText Name,
    Guid? BrandId,
    Guid? ManufacturerId,
    string DosageFormCode,
    IReadOnlyList<string> RouteCodes,
    IReadOnlyList<string> ClassCodes,
    IReadOnlyList<NewIngredientRef> Ingredients,
    IReadOnlyList<string> Synonyms,
    IReadOnlyList<NewIdentifier> Identifiers,
    IReadOnlyList<NewStatement> Statements,
    bool IsDemo);

public sealed record NewIngredient(LocalizedText Name, string? AtcCode, IReadOnlyList<string> Synonyms);

public sealed record NewManufacturer(LocalizedText Name, string? Country, string? ManufacturerCode);

public sealed record NewBrand(LocalizedText Name, Guid? ManufacturerId);

public sealed record NewReferenceTerm(ReferenceKind Kind, string Code, LocalizedText Name);

public sealed record NewInteraction(Guid IngredientAId, Guid IngredientBId, InteractionSeverity Severity, LocalizedText Mechanism, LocalizedText Management, Guid RevisionId);

public sealed record NewKnowledgeSource(
    string Name,
    string Publisher,
    SourceType Type,
    string? Url,
    string Version,
    string LicenseName,
    bool? RedistributionAllowed,
    string? UsageRestrictions);

public sealed record NewRevision(Guid SourceId, string Label, string? Notes);

public sealed record MedicationVersionDto(int Version, DateTimeOffset ChangedAt, Guid? ChangedBy, string ChangeReason);

// ---------- results ----------

public enum MedicationError
{
    None,
    NotFound,
    Validation,
    Conflict,
    Forbidden,
}

/// <param name="Detail">Machine-readable codes only (e.g. "names.required;gtin.checksum"); never user text or internals.</param>
public sealed record OperationResult<T>(T? Value, MedicationError Error, string? Detail = null)
{
    public bool Succeeded => Error == MedicationError.None;
}

public static class OperationResult
{
    public static OperationResult<T> Ok<T>(T value) => new(value, MedicationError.None);
    public static OperationResult<T> Fail<T>(MedicationError error, string? detail = null) => new(default, error, detail);
}

// ---------- services ----------

public interface IMedicationService
{
    Task<PagedResult<MedicationSummaryDto>> SearchAsync(MedicationSearchQuery query, CancellationToken ct = default);
    /// <param name="includeNonActive">Draft/inactive records are invisible to ordinary readers; only editors may ask for them.</param>
    Task<OperationResult<MedicationDetailDto>> GetAsync(Guid id, bool includeNonActive = false, CancellationToken ct = default);
    Task<OperationResult<MedicationKnowledgeDocument>> GetKnowledgeDocumentAsync(Guid id, bool includeNonActive = false, CancellationToken ct = default);

    /// <summary>A manufacturer from the reference (used to check that a product's manufacturer is compatible with its medication).</summary>
    Task<OperationResult<ManufacturerDto>> GetManufacturerAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Controlled editing. Every method audits; callers must already hold the permission (checked at the API edge).</summary>
public interface IMedicationAdminService
{
    Task<OperationResult<MedicationDetailDto>> CreateAsync(Guid actorUserId, MedicationDraft draft, string source, string? correlationId, CancellationToken ct = default);
    Task<OperationResult<MedicationDetailDto>> UpdateAsync(Guid actorUserId, Guid id, int expectedVersion, MedicationDraft draft, string reason, string source, string? correlationId, CancellationToken ct = default);
    Task<OperationResult<MedicationDetailDto>> SetLifecycleAsync(Guid actorUserId, Guid id, int expectedVersion, LifecycleStatus status, string source, string? correlationId, CancellationToken ct = default);
    Task<OperationResult<MedicationDetailDto>> SetValidationAsync(Guid actorUserId, Guid id, int expectedVersion, ValidationStatus status, Guid revisionId, string source, string? correlationId, CancellationToken ct = default);
    Task<OperationResult<IReadOnlyList<MedicationVersionDto>>> ListVersionsAsync(Guid id, CancellationToken ct = default);

    Task<OperationResult<IngredientDto>> CreateIngredientAsync(Guid actorUserId, NewIngredient value, CancellationToken ct = default);
    Task<OperationResult<ManufacturerDto>> CreateManufacturerAsync(Guid actorUserId, NewManufacturer value, CancellationToken ct = default);
    Task<OperationResult<BrandDto>> CreateBrandAsync(Guid actorUserId, NewBrand value, CancellationToken ct = default);
    Task<OperationResult<ReferenceTermDto>> CreateReferenceTermAsync(Guid actorUserId, NewReferenceTerm value, CancellationToken ct = default);
    Task<OperationResult<InteractionDto>> UpsertInteractionAsync(Guid actorUserId, NewInteraction value, string source, string? correlationId, CancellationToken ct = default);
}

public interface IKnowledgeSourceService
{
    Task<OperationResult<KnowledgeSourceDto>> RegisterSourceAsync(Guid actorUserId, NewKnowledgeSource value, string source, string? correlationId, CancellationToken ct = default);
    Task<OperationResult<KnowledgeRevisionDto>> AddRevisionAsync(Guid actorUserId, NewRevision value, string source, string? correlationId, CancellationToken ct = default);
    Task<OperationResult<KnowledgeRevisionDto>> SetRevisionStatusAsync(Guid actorUserId, Guid revisionId, RevisionStatus status, string source, string? correlationId, CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgeSourceDto>> ListSourcesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgeRevisionDto>> ListRevisionsAsync(Guid sourceId, CancellationToken ct = default);
}

/// <summary>Development-only seeding of FICTIONAL medications. Registered only when demo seeding is enabled.</summary>
public interface IDemoMedicationSeeder
{
    Task SeedAsync(CancellationToken ct = default);
}
