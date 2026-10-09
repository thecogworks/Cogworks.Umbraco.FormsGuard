using System.Reflection;
using Cogworks.Umbraco.FormsGuard.Persistence.Dtos;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NPoco;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseModelDefinitions;
using Umbraco.Cms.Infrastructure.Persistence.Mappers;
using Umbraco.Cms.Persistence.Sqlite.Services;
using Umbraco.Cms.Infrastructure.Scoping;

namespace Cogworks.Umbraco.FormsGuard.Tests.Persistence;

/// <summary>An in-memory SQLite database with the Forms Guard tables, behind a fake scope provider.</summary>
public sealed class SqliteHarness : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteHarness()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var syntax = new SqliteSyntaxProvider(Options.Create(new GlobalSettings()), NullLogger<SqliteSyntaxProvider>.Instance);
        var npocoMappers = new NPoco.MapperCollection { new NullableDateMapper() };
        npocoMappers.AddRange(new SqliteSpecificMapperFactory().Mappers);
        var pocoDataFactory = new FluentPocoDataFactory(
            (type, iPocoDataFactory) => new PocoDataBuilder(type, npocoMappers).Init(),
            npocoMappers);
        SqlContext = new SqlContext(syntax, DatabaseType.SQLite, pocoDataFactory, new global::Umbraco.Cms.Infrastructure.Persistence.Mappers.MapperCollection(() => []));

        Database = CreateDatabase(_connection, SqlContext);
        foreach (var m in npocoMappers) { Database.Mappers.Add(m); }

        foreach (var dto in new[] { typeof(DecisionDto), typeof(AuditDto), typeof(FormSettingsDto), typeof(RuleDto) })
        {
            var table = DefinitionFactory.GetTableDefinition(dto, syntax);
            Database.Execute(new Sql(syntax.Format(table)));
            foreach (var sql in syntax.Format(table.Indexes))
            {
                Database.Execute(new Sql(sql));
            }
        }

        ScopeProvider = ScopeProxy.CreateProvider(Database, SqlContext);
    }

    public ISqlContext SqlContext { get; }

    public IUmbracoDatabase Database { get; }

    public IScopeProvider ScopeProvider { get; }

    public void Dispose() => _connection.Dispose();

    // UmbracoDatabase(DbConnection, ISqlContext, ILogger, IBulkSqlInsertProvider) is internal; call it by reflection.
    private static UmbracoDatabase CreateDatabase(SqliteConnection connection, ISqlContext sqlContext)
    {
        var ctor = typeof(UmbracoDatabase).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(c => c.GetParameters().FirstOrDefault()?.ParameterType == typeof(System.Data.Common.DbConnection));
        return (UmbracoDatabase)ctor.Invoke([connection, sqlContext, NullLogger<UmbracoDatabase>.Instance, null]);
    }
}

/// <summary>Fakes <see cref="IScopeProvider"/> and <see cref="IScope"/>: every scope shares the one database; Complete is a no-op.</summary>
public class ScopeProxy : DispatchProxy
{
    private IUmbracoDatabase _database = null!;
    private ISqlContext _sqlContext = null!;
    private bool _isProvider;

    public static IScopeProvider CreateProvider(IUmbracoDatabase database, ISqlContext sqlContext)
    {
        var provider = Create<IScopeProvider, ScopeProxy>();
        var proxy = (ScopeProxy)(object)provider;
        (proxy._database, proxy._sqlContext, proxy._isProvider) = (database, sqlContext, true);
        return provider;
    }

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        switch (method!.Name)
        {
            case "CreateScope" when _isProvider:
                var scope = Create<IScope, ScopeProxy>();
                var proxy = (ScopeProxy)(object)scope;
                (proxy._database, proxy._sqlContext) = (_database, _sqlContext);
                return scope;
            case "get_SqlContext":
                return _sqlContext;
            case "get_Database":
                return _database;
            case "Complete":
                return true;
            case "Dispose":
                return null;
            default:
                throw new NotSupportedException(method.Name);
        }
    }
}
