using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace Cogworks.Umbraco.FormsGuard.Persistence.Dtos;

/// <summary>Per-form Forms Guard settings, keyed by form ID.</summary>
[TableName(FormsGuardTables.FormSettings)]
[PrimaryKey("FormId", AutoIncrement = false)]
[ExplicitColumns]
public class FormSettingsDto
{
    [Column("FormId")]
    [PrimaryKeyColumn(AutoIncrement = false)]
    public Guid FormId { get; set; }

    [Column("Guarded")]
    public bool Guarded { get; set; }

    /// <summary>JSON: organisation, allowlist, questions, thresholds, failure policy.</summary>
    [Column("Settings")]
    [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
    public string Settings { get; set; } = "{}";

    [Column("UpdatedUtc")]
    public DateTime UpdatedUtc { get; set; }
}
