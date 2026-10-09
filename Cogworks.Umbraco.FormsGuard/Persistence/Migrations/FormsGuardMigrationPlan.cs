using Cogworks.Umbraco.FormsGuard.Persistence.Dtos;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Migrations;
using Umbraco.Cms.Core.Packaging;

namespace Cogworks.Umbraco.FormsGuard.Persistence.Migrations;

/// <summary>Package migration plan for Forms Guard tables. Discovered by the type loader.</summary>
public class FormsGuardMigrationPlan : PackageMigrationPlan
{
    public const string PlanName = "Cogworks.Umbraco.FormsGuard";

    public FormsGuardMigrationPlan()
        : base(PlanName)
    {
    }

    protected override void DefinePlan()
    {
        From(string.Empty)
            .To<CreateTables>("cogFormsGuard-tables-v1");
    }
}

/// <summary>Creates the Forms Guard tables, skipping any that already exist.</summary>
public class CreateTables : AsyncMigrationBase
{
    public CreateTables(IMigrationContext context)
        : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        CreateIfMissing<DecisionDto>(FormsGuardTables.Decision);
        CreateIfMissing<FormSettingsDto>(FormsGuardTables.FormSettings);
        CreateIfMissing<RuleDto>(FormsGuardTables.Rule);
        CreateIfMissing<AuditDto>(FormsGuardTables.Audit);
        return Task.CompletedTask;
    }

    private void CreateIfMissing<TDto>(string tableName)
    {
        if (TableExists(tableName))
        {
            Logger.LogDebug("Table {TableName} already exists; skipping", tableName);
            return;
        }

        Create.Table<TDto>().Do();
    }
}
