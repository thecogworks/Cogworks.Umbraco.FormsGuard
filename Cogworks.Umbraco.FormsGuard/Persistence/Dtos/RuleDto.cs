using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace Cogworks.Umbraco.FormsGuard.Persistence.Dtos;

/// <summary>A hard rule for a form: blocked domain, allowed domain or blocked phrase.</summary>
[TableName(FormsGuardTables.Rule)]
[PrimaryKey("Id", AutoIncrement = true)]
[ExplicitColumns]
public class RuleDto
{
    [Column("Id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("FormId")]
    [Index(IndexTypes.NonClustered)]
    public Guid FormId { get; set; }

    /// <summary><c>BlockedDomain</c>, <c>AllowedDomain</c> or <c>BlockedPhrase</c>.</summary>
    [Column("RuleType")]
    [Length(32)]
    public string RuleType { get; set; } = string.Empty;

    [Column("Pattern")]
    [Length(400)]
    public string Pattern { get; set; } = string.Empty;

    [Column("CreatedUtc")]
    public DateTime CreatedUtc { get; set; }
}
