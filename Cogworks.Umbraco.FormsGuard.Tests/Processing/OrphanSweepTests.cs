using Cogworks.Umbraco.FormsGuard.Configuration;
using Cogworks.Umbraco.FormsGuard.Processing;
using Cogworks.Umbraco.FormsGuard.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Cogworks.Umbraco.FormsGuard.Tests.Processing;

public class OrphanSweepTests
{
    [Fact]
    public void NoOrphans_DoesNotDelete()
    {
        var repo = new SweepRepo();

        Sweep(repo, batchSize: 10).Run();

        Assert.Equal(100, repo.Take);
        Assert.Empty(repo.Deletes);
    }

    [Fact]
    public void Orphans_DeletedWithMissingDetail()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var repo = new SweepRepo { Orphans = ids };

        Sweep(repo, batchSize: 10).Run();

        var call = Assert.Single(repo.Deletes);
        Assert.Equal(ids, call.Ids);
        Assert.Equal("Forms record missing", call.Detail);
    }

    [Fact]
    public void BatchSizeZero_TakesAtLeastOne()
    {
        var repo = new SweepRepo();

        Sweep(repo, batchSize: 0).Run();

        Assert.Equal(1, repo.Take);
    }

    [Fact]
    public void RepositoryThrows_DoesNotThrow()
    {
        var repo = new SweepRepo { Throw = true };

        var ex = Record.Exception(() => Sweep(repo, batchSize: 10).Run());

        Assert.Null(ex);
    }

    private static OrphanSweep Sweep(SweepRepo repo, int batchSize) =>
        new(repo, new StaticOptions(new FormsGuardOptions { ProcessorBatchSize = batchSize }), NullLogger<OrphanSweep>.Instance);

    private sealed class SweepRepo : RepositoryStub
    {
        public IReadOnlyList<Guid> Orphans { get; init; } = [];
        public bool Throw { get; init; }
        public int Take { get; private set; }
        public List<(Guid[] Ids, string Detail)> Deletes { get; } = [];

        public override IReadOnlyList<Guid> GetOrphanRecordIds(int take)
        {
            if (Throw)
            {
                throw new InvalidOperationException("db down");
            }

            Take = take;
            return Orphans;
        }

        public override int DeleteForRecords(IReadOnlyCollection<Guid> recordIds, string detail)
        {
            Deletes.Add((recordIds.ToArray(), detail));
            return recordIds.Count;
        }
    }

    private sealed class StaticOptions(FormsGuardOptions value) : IOptionsMonitor<FormsGuardOptions>
    {
        public FormsGuardOptions CurrentValue { get; } = value;

        public FormsGuardOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<FormsGuardOptions, string?> listener) => null;
    }
}
