using MedSmarter.Modules.Medications.Contracts;

namespace MedSmarter.Modules.Medications.Domain;

// Persistence-neutral domain model (see docs/phase4/02-data-model.md). Mapped to PostgreSQL by MedicationsDbContext.
// Clinical text is ALWAYS tied to a KnowledgeRevision: no revision, no statement.

/// <summary>DosageForm, Route, TherapeuticClass and DrugClass share one shape, so one table with a discriminator.</summary>
public sealed class ReferenceTerm
{
    public Guid Id { get; set; }
    public ReferenceKind Kind { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string? NameFa { get; set; }
}

public sealed class Manufacturer
{
    public Guid Id { get; set; }
    public string? NameEn { get; set; }
    public string? NameFa { get; set; }
    public string? Country { get; set; }
    public string? ManufacturerCode { get; set; }
}

public sealed class Brand
{
    public Guid Id { get; set; }
    public string? NameEn { get; set; }
    public string? NameFa { get; set; }
    public Guid? ManufacturerId { get; set; }
}

public sealed class IngredientSynonym
{
    public Guid Id { get; set; }
    public Guid IngredientId { get; set; }
    public string Text { get; set; } = string.Empty;
}

public sealed class ActiveIngredient
{
    public Guid Id { get; set; }
    public string? NameEn { get; set; }
    public string? NameFa { get; set; }
    public string? AtcCode { get; set; }
    public LifecycleStatus Lifecycle { get; set; } = LifecycleStatus.Active;
    public ValidationStatus Validation { get; set; } = ValidationStatus.Unverified;
    public bool IsDemo { get; set; }
    public List<IngredientSynonym> Synonyms { get; set; } = [];
}

public sealed class KnowledgeSource
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public SourceType Type { get; set; }
    public string? Url { get; set; }
    public string Version { get; set; } = string.Empty;
    public string LicenseName { get; set; } = string.Empty;
    /// <summary>null = not yet checked. Only an explicit <c>true</c> allows the content to be shown as verified.</summary>
    public bool? RedistributionAllowed { get; set; }
    public string? UsageRestrictions { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public ValidationStatus Validation { get; set; } = ValidationStatus.Unverified;
}

public sealed class KnowledgeRevision
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public string Label { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; set; }
    public RevisionStatus Status { get; set; } = RevisionStatus.Draft;
    public string? Notes { get; set; }
}

public sealed class MedicationIngredient
{
    public Guid MedicationId { get; set; }
    public Guid IngredientId { get; set; }
    public decimal? StrengthValue { get; set; }
    public string? StrengthUnit { get; set; }
    public string? PerUnit { get; set; }
    public int Order { get; set; }
}

public sealed class MedicationRoute
{
    public Guid MedicationId { get; set; }
    public Guid TermId { get; set; }
}

public sealed class MedicationClassification
{
    public Guid MedicationId { get; set; }
    public Guid TermId { get; set; }
}

public sealed class MedicationSynonym
{
    public Guid Id { get; set; }
    public Guid MedicationId { get; set; }
    public string Text { get; set; } = string.Empty;
}

public sealed class MedicationIdentifier
{
    public Guid Id { get; set; }
    public Guid MedicationId { get; set; }
    public IdentifierScheme Scheme { get; set; }
    public string Value { get; set; } = string.Empty;
    public Guid SourceRevisionId { get; set; }
}

public sealed class MedicationStatement
{
    public Guid Id { get; set; }
    public Guid MedicationId { get; set; }
    public StatementKind Kind { get; set; }
    public string? TextEn { get; set; }
    public string? TextFa { get; set; }
    public string? Severity { get; set; }
    public string? Frequency { get; set; }
    public string? Population { get; set; }
    public Guid RevisionId { get; set; }
}

/// <summary>Interaction between two active ingredients; (A,B) is stored once with A &lt; B.</summary>
public sealed class DrugInteraction
{
    public Guid Id { get; set; }
    public Guid IngredientAId { get; set; }
    public Guid IngredientBId { get; set; }
    public InteractionSeverity Severity { get; set; }
    public string? MechanismEn { get; set; }
    public string? MechanismFa { get; set; }
    public string? ManagementEn { get; set; }
    public string? ManagementFa { get; set; }
    public Guid RevisionId { get; set; }
}

public sealed class Medication
{
    public Guid Id { get; set; }
    public int Version { get; set; } = 1;
    public string? NameEn { get; set; }
    public string? NameFa { get; set; }
    public Guid? BrandId { get; set; }
    public Guid? ManufacturerId { get; set; }
    public Guid DosageFormId { get; set; }
    public LifecycleStatus Lifecycle { get; set; } = LifecycleStatus.Draft;
    public ValidationStatus Validation { get; set; } = ValidationStatus.Unverified;
    public bool IsDemo { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public List<MedicationIngredient> Ingredients { get; set; } = [];
    public List<MedicationRoute> Routes { get; set; } = [];
    public List<MedicationClassification> Classifications { get; set; } = [];
    public List<MedicationSynonym> Synonyms { get; set; } = [];
    public List<MedicationIdentifier> Identifiers { get; set; } = [];
    public List<MedicationStatement> Statements { get; set; } = [];
}

/// <summary>Append-only history: the full detail view as it was at <see cref="VersionNumber"/>.</summary>
public sealed class MedicationVersion
{
    public Guid Id { get; set; }
    public Guid MedicationId { get; set; }
    public int VersionNumber { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public DateTimeOffset ChangedAt { get; set; }
    public Guid? ChangedBy { get; set; }
    public string ChangeReason { get; set; } = string.Empty;
}

/// <summary>Derived search index: rebuilt from names, brand, ingredients, synonyms and identifiers on every save.</summary>
public sealed class MedicationSearchTerm
{
    public Guid Id { get; set; }
    public Guid MedicationId { get; set; }
    public string Normalized { get; set; } = string.Empty;
    public string Display { get; set; } = string.Empty;
    public NameKind Kind { get; set; }
}
