namespace GHOST.Models;

public sealed class CaseFile
{
    public int Id { get; set; }
    public string CaseNumber { get; set; } = $"CASE-{Guid.NewGuid():N}"[..13].ToUpperInvariant();
    public string Title { get; set; } = "Untitled investigation";
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class Experiment
{
    public int Id { get; set; }
    public int CaseFileId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = "Scientific";
    public string Hypothesis { get; set; } = string.Empty;
    public string Procedure { get; set; } = string.Empty;
    public string Results { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class EvidenceItem
{
    public int Id { get; set; }
    public int CaseFileId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string EvidenceType { get; set; } = "File";
    public string RelativePath { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
    public bool IsArchived { get; set; }
}

public sealed class Observation
{
    public int Id { get; set; }
    public int ExperimentId { get; set; }
    public string Description { get; set; } = string.Empty;
    public double? NumericValue { get; set; }
    public string Unit { get; set; } = string.Empty;
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}

public sealed class EvidenceRelationship
{
    public int Id { get; set; }
    public string SourceType { get; set; } = "Evidence";
    public int SourceId { get; set; }
    public string TargetType { get; set; } = "Evidence";
    public int TargetId { get; set; }
    public string RelationshipType { get; set; } = "Related to";
    public string Description { get; set; } = string.Empty;
    public double Confidence { get; set; } = 0.5;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class TimelineEvent
{
    public int Id { get; set; }
    public int CaseFileId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

public sealed class AuditEntry
{
    public int Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
