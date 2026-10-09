using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace Cogworks.Umbraco.FormsGuard.Persistence.Dtos;

/// <summary>One decision per Forms record. Holds no field values or personal data.</summary>
[TableName(FormsGuardTables.Decision)]
[PrimaryKey("Id", AutoIncrement = true)]
[ExplicitColumns]
public class DecisionDto
{
    [Column("Id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("RecordId")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid RecordId { get; set; }

    [Column("FormId")]
    [Index(IndexTypes.NonClustered)]
    public Guid FormId { get; set; }

    [Column("Status")]
    [Length(32)]
    public string Status { get; set; } = string.Empty;

    /// <summary><c>rule</c>, <c>provider</c>, <c>policy</c>, <c>record</c> or <c>settings</c> (the form's setup decided, for example no questions to ask).</summary>
    [Column("Source")]
    [Length(16)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Source { get; set; }

    /// <summary>Rule type and rule ID; never the matched text.</summary>
    [Column("RuleHit")]
    [Length(255)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? RuleHit { get; set; }

    [Column("Provider")]
    [Length(64)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Provider { get; set; }

    [Column("ModelVersion")]
    [Length(64)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ModelVersion { get; set; }

    /// <summary>JSON object of question key to probability.</summary>
    [Column("Probabilities")]
    [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Probabilities { get; set; }

    [Column("Attempts")]
    public int Attempts { get; set; }

    [Column("NextAttemptUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? NextAttemptUtc { get; set; }

    [Column("ClaimedBy")]
    [Length(255)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ClaimedBy { get; set; }

    [Column("ClaimedUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? ClaimedUtc { get; set; }

    [Column("Reviewer")]
    [Length(255)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Reviewer { get; set; }

    [Column("CreatedUtc")]
    public DateTime CreatedUtc { get; set; }

    [Column("UpdatedUtc")]
    public DateTime UpdatedUtc { get; set; }
}
