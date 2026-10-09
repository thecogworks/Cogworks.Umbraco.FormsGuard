using System.Reflection;
using Cogworks.Umbraco.FormsGuard.Persistence.Dtos;
using NPoco;

namespace Cogworks.Umbraco.FormsGuard.Tests.Persistence;

public class SchemaTests
{
    private static readonly string[] ForbiddenFragments = ["email", "phone", "name", "value"];

    private static IEnumerable<Type> FormsGuardDtos() =>
        typeof(FormsGuardTables).Assembly
            .GetTypes()
            .Where(t => t.GetCustomAttribute<TableNameAttribute>()?.Value
                ?.StartsWith(FormsGuardTables.Prefix, StringComparison.Ordinal) == true);

    [Fact]
    public void ExactlyFourFormsGuardTables()
    {
        var tables = FormsGuardDtos()
            .Select(t => t.GetCustomAttribute<TableNameAttribute>()!.Value)
            .OrderBy(n => n)
            .ToArray();

        Assert.Equal(
            new[]
            {
                FormsGuardTables.Audit,
                FormsGuardTables.Decision,
                FormsGuardTables.FormSettings,
                FormsGuardTables.Rule,
            }.OrderBy(n => n),
            tables);
    }

    [Fact]
    public void NoColumnLooksLikePersonalData()
    {
        var offenders = (
            from dto in FormsGuardDtos()
            from property in dto.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            let column = property.GetCustomAttribute<ColumnAttribute>()?.Name ?? property.Name
            where ForbiddenFragments.Any(f => column.Contains(f, StringComparison.OrdinalIgnoreCase))
            select $"{dto.GetCustomAttribute<TableNameAttribute>()!.Value}.{column}").ToList();

        Assert.True(offenders.Count == 0, "PII-like column(s): " + string.Join(", ", offenders));
    }
}
