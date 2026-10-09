using System.Reflection;
using Cogworks.Umbraco.FormsGuard.Configuration;
using Cogworks.Umbraco.FormsGuard.Persistence;
using Cogworks.Umbraco.FormsGuard.Persistence.Dtos;
using Cogworks.Umbraco.FormsGuard.Processing;
using Cogworks.Umbraco.FormsGuard.Tests.Fakes;
using Cogworks.Umbraco.FormsGuard.Tests.Review;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Umbraco.Forms.Core.Data.Storage;
using Umbraco.Forms.Core.Enums;
using Umbraco.Forms.Core.Services;
using FormsForm = Umbraco.Forms.Core.Models.Form;
using FormsRecord = Umbraco.Forms.Core.Persistence.Dtos.Record;

namespace Cogworks.Umbraco.FormsGuard.Tests.Processing;

/// <summary>The purge loop with in-memory fakes; the Quarantined/cut-off SQL filter is in <c>RepositorySqlTests</c>.</summary>
public class QuarantinePurgeTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(30)]
    [InlineData(1)]
    public void CutoffUtc_PositiveDays_IsThatManyDaysBeforeNow(int days)
    {
        Assert.Equal(Now.AddDays(-days), QuarantinePurge.CutoffUtc(Now, days));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CutoffUtc_ZeroOrNegative_IsOff(int days)
    {
        Assert.Null(QuarantinePurge.CutoffUtc(Now, days));
    }

    [Fact]
    public async Task Expired_DeletedThroughFormsWithPurgeAudit()
    {
        var h = new Harness(rowCount: 1);

        await h.Purge(days: 30).RunAsync(default);

        var call = Assert.Single(h.DeleteCalls);
        Assert.Equal("DeleteAsync", call.Method);
        Assert.Same(h.Records[0], call.Record);
        var audit = Assert.Single(h.Repo.Audits);
        Assert.Equal((h.Rows[0].Id, DecisionRepository.QuarantinePurgeAction, "Quarantined for more than 30 days"), audit);
    }

    [Fact]
    public async Task Disabled_DoesNotQueryOrDelete()
    {
        var h = new Harness(rowCount: 1);

        await h.Purge(days: 0).RunAsync(default);

        Assert.Equal(0, h.Repo.Queries);
        Assert.Empty(h.DeleteCalls);
    }

    [Fact]
    public async Task RecordGone_SkippedWithoutDeleteOrAudit()
    {
        var h = new Harness(rowCount: 1, recordExists: false);

        await h.Purge(days: 30).RunAsync(default);

        Assert.Empty(h.DeleteCalls);
        Assert.Empty(h.Repo.Audits);
    }

    [Fact]
    public async Task RestoredAfterBatchRead_NotDeleted()
    {
        var h = new Harness(rowCount: 1);
        h.RecordStorageCalled = () => h.Rows[0].Status = "Approved";

        await h.Purge(days: 30).RunAsync(default);

        Assert.Empty(h.DeleteCalls);
        Assert.Empty(h.Repo.Audits);
    }

    [Fact]
    public async Task ApprovedInForms_SkippedWithoutDeleteOrAudit()
    {
        var h = new Harness(rowCount: 1);
        h.Records[0].State = FormState.Approved;

        await h.Purge(days: 30).RunAsync(default);

        Assert.Empty(h.DeleteCalls);
        Assert.Empty(h.Repo.Audits);
        Assert.Equal("Quarantined", h.Rows[0].Status);
    }

    [Fact]
    public async Task InsideRetentionWindow_NotDeleted()
    {
        var h = new Harness(rowCount: 2);
        h.Rows[0].UpdatedUtc = DateTime.UtcNow.AddDays(-29);

        await h.Purge(days: 30).RunAsync(default);

        var call = Assert.Single(h.DeleteCalls);
        Assert.Same(h.Records[1], call.Record);
    }

    [Fact]
    public async Task DeleteFails_NoAuditAndNextRowStillPurged()
    {
        var h = new Harness(rowCount: 2, failFirstDelete: true);

        await h.Purge(days: 30).RunAsync(default);

        Assert.Equal(2, h.DeleteCalls.Count);
        var audit = Assert.Single(h.Repo.Audits);
        Assert.Equal(h.Rows[1].Id, audit.Id);
    }

    [Fact]
    public async Task FullBatch_ReadsNextBatchAfterLastId()
    {
        var h = new Harness(rowCount: 150);

        await h.Purge(days: 30).RunAsync(default);

        Assert.Equal(150, h.DeleteCalls.Count);
        Assert.Equal([0, 100], h.Repo.AfterIds);
    }

    private sealed class Harness
    {
        public Harness(int rowCount, bool recordExists = true, bool failFirstDelete = false)
        {
            var form = new FormsForm { Id = Guid.NewGuid(), Name = "Contact" };
            Records = Enumerable.Range(0, rowCount).Select(_ => new FormsRecord { UniqueId = Guid.NewGuid(), Form = form.Id, State = FormState.Rejected }).ToList();
            Rows = Records.Select((r, i) => new DecisionDto { Id = i + 1, RecordId = r.UniqueId, FormId = form.Id, Status = "Quarantined" }).ToList();
            Repo = new PurgeRepo(Rows);
            FormService = ReviewServiceTests.Fake<IFormService>.Create("Get", _ => form);
            RecordStorage = ReviewServiceTests.Fake<IRecordStorage>.Create(
                "GetRecordByUniqueId", args =>
                {
                    RecordStorageCalled?.Invoke();
                    return recordExists ? Records.First(r => Equals(r.UniqueId, args[0])) : null;
                });
            RecordService = DeletingRecordService.Create(DeleteCalls, failFirstDelete);
        }

        /// <summary>Runs after the batch is read and before the re-check, to change a row mid-run.</summary>
        public Action? RecordStorageCalled { get; set; }

        public List<FormsRecord> Records { get; }
        public List<DecisionDto> Rows { get; }
        public PurgeRepo Repo { get; }
        public List<(string Method, object? Record)> DeleteCalls { get; } = [];
        private IFormService FormService { get; }
        private IRecordStorage RecordStorage { get; }
        private IRecordService RecordService { get; }

        public QuarantinePurge Purge(int days) => new(
            Repo, FormService, RecordStorage, RecordService,
            new StaticOptions(new FormsGuardOptions { QuarantineRetentionDays = days }),
            NullLogger<QuarantinePurge>.Instance);
    }

    private sealed class PurgeRepo(List<DecisionDto> rows) : RepositoryStub
    {
        public int Queries { get; private set; }
        public List<int> AfterIds { get; } = [];
        public List<(int Id, string Action, string Detail)> Audits { get; } = [];

        /// <summary>Ignores <paramref name="cutoffUtc"/>, so the purge's own re-check of the cut-off is what is tested.</summary>
        public override IReadOnlyList<DecisionDto> GetQuarantinedBefore(DateTime cutoffUtc, int afterId, int take)
        {
            Queries++;
            AfterIds.Add(afterId);
            return rows.Where(r => r.Id > afterId).OrderBy(r => r.Id).Take(take).ToList();
        }

        public override DecisionDto? GetByRecordId(Guid recordId) => rows.FirstOrDefault(r => r.RecordId == recordId);

        public override void InsertSystemAudit(DecisionDto row, string action, string detail) =>
            Audits.Add((row.Id, action, detail));
    }

    /// <summary>Records <see cref="IRecordService"/> calls; optionally the first one fails.</summary>
    public class DeletingRecordService : DispatchProxy
    {
        private List<(string Method, object? Record)> _calls = [];
        private bool _failNext;

        public static IRecordService Create(List<(string Method, object? Record)> calls, bool failFirst)
        {
            var proxy = DispatchProxy.Create<IRecordService, DeletingRecordService>();
            var fake = (DeletingRecordService)(object)proxy;
            fake._calls = calls;
            fake._failNext = failFirst;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            _calls.Add((targetMethod!.Name, args?.ElementAtOrDefault(0)));
            if (_failNext)
            {
                _failNext = false;
                return Task.FromException(new InvalidOperationException("delete failed"));
            }

            return Task.CompletedTask;
        }
    }

    private sealed class StaticOptions(FormsGuardOptions value) : IOptionsMonitor<FormsGuardOptions>
    {
        public FormsGuardOptions CurrentValue { get; } = value;

        public FormsGuardOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<FormsGuardOptions, string?> listener) => null;
    }
}
