using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace Cogworks.Umbraco.FormsGuard.Persistence.Dtos;

/// <summary>Audit log entry. Holds no field values or personal data.</summary>
[TableName(FormsGuardTables.Audit)]
[PrimaryKey("Id", AutoIncrement = true)]
[ExplicitColumns]
public class AuditDto
{
    [Column("Id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("RecordId")]
    [Index(IndexTypes.NonClustered)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? RecordId { get; set; }

    [Column("FormId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? FormId { get; set; }

    [Column("Action")]
    [Length(64)]
    public string Action { get; set; } = string.Empty;

    /// <summary>Backoffice user key or <c>system</c>.</summary>
    [Column("Actor")]
    [Length(255)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Actor { get; set; }

    [Column("Detail")]
    [Length(1000)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Detail { get; set; }

    [Column("CreatedUtc")]
    public DateTime CreatedUtc { get; set; }
}
