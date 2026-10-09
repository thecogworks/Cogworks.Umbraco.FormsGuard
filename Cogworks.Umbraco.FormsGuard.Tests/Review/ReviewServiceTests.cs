using System.Reflection;
using Cogworks.Umbraco.FormsGuard.Decisions;
using Cogworks.Umbraco.FormsGuard.Persistence;
using Cogworks.Umbraco.FormsGuard.Persistence.Dtos;
using Cogworks.Umbraco.FormsGuard.Processing;
using Cogworks.Umbraco.FormsGuard.Review;
using Cogworks.Umbraco.FormsGuard.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Umbraco.Forms.Core.Data.Storage;
using Umbraco.Forms.Core.Enums;
using Umbraco.Forms.Core.Services;
using FormsForm = Umbraco.Forms.Core.Models.Form;
using FormsRecord = Umbraco.Forms.Core.Persistence.Dtos.Record;

namespace Cogworks.Umbraco.FormsGuard.Tests.Review;

/// <summary>The plan's matrix at service level, with in-memory fakes; the repository SQL is not covered here.</summary>
public class ReviewServiceTests
{
    private static readonly Guid ReviewerKey = Guid.NewGuid();

    [Fact]
    public async Task Approve_FromReview_IsDone()
    {
        var h = new Harness(DecisionStatus.Review, FormState.Submitted, previousReviewer: "old");

        var result = await h.Service.ApproveAsync(h.Row.RecordId, ReviewerKey, default);

        Assert.Equal(new ReviewResult(ReviewOutcome.Done, DecisionStatus.Approved), result);
        var call = Assert.Single(h.Repo.TryReviews);
        Assert.Equal((DecisionStatus.Review, DecisionStatus.Approved, ReviewerKey.ToString(), "approve"), call);
        Assert.Equal(new[] { DecisionStatus.Approved }, h.Applier.Applied);
        var outcome = Assert.Single(h.Applier.Handled);
        Assert.Equal(DecisionStatus.Approved, outcome.Status);
        Assert.Empty(outcome.Answers);
        Assert.Empty(h.Repo.Reverts);
    }

    [Fact]
    public async Task Handlers_GetNoneToken_EvenWhenRequestIsCancelled()
    {
        var h = new Harness(DecisionStatus.Review, FormState.Submitted);
        using var request = new CancellationTokenSource();
        request.Cancel();

        await h.Service.ConfirmSpamAsync(h.Row.RecordId, ReviewerKey, request.Token);

        Assert.Equal(CancellationToken.None, Assert.Single(h.Applier.HandlerTokens));
    }

    [Fact]
    public async Task ConfirmSpam_FromReview_Rejects()
    {
        var h = new Harness(DecisionStatus.Review, FormState.Submitted);

        var result = await h.Service.ConfirmSpamAsync(h.Row.RecordId, ReviewerKey, default);

        Assert.Equal(new ReviewResult(ReviewOutcome.Done, DecisionStatus.Quarantined), result);
        Assert.Equal("confirm-spam", Assert.Single(h.Repo.TryReviews).Action);
        Assert.Equal(new[] { DecisionStatus.Quarantined }, h.Applier.Applied);
    }

    [Fact]
    public async Task Restore_FromQuarantined_ApprovesRejectedRecord()
    {
        var h = new Harness(DecisionStatus.Quarantined, FormState.Rejected);

        var result = await h.Service.RestoreAsync(h.Row.RecordId, ReviewerKey, default);

        Assert.Equal(new ReviewResult(ReviewOutcome.Done, DecisionStatus.Approved), result);
        Assert.Equal((DecisionStatus.Quarantined, DecisionStatus.Approved, ReviewerKey.ToString(), "restore"), Assert.Single(h.Repo.TryReviews));
        Assert.Equal(new[] { DecisionStatus.Approved }, h.Applier.Applied);
        Assert.Single(h.Applier.Handled);
    }

    [Fact]
    public async Task Approve_RecordAlreadyApproved_SkipsRecordCall()
    {
        var h = new Harness(DecisionStatus.Review, FormState.Approved);

        var result = await h.Service.ApproveAsync(h.Row.RecordId, ReviewerKey, default);

        Assert.Equal(ReviewOutcome.Done, result.Outcome);
        Assert.Single(h.Repo.TryReviews);
        Assert.Empty(h.Applier.Applied);
        Assert.Single(h.Applier.Handled);
    }

    [Theory]
    [InlineData(ReviewAction.Restore, DecisionStatus.Review)]
    [InlineData(ReviewAction.Approve, DecisionStatus.Pending)]
    [InlineData(ReviewAction.Approve, DecisionStatus.Approved)]
    [InlineData(ReviewAction.ConfirmSpam, DecisionStatus.Quarantined)]
    public async Task NotAllowed_IsRefused_NoWrites(ReviewAction action, DecisionStatus from)
    {
        var h = new Harness(from, FormState.Submitted);

        var result = await Run(h, action);

        Assert.Equal(new ReviewResult(ReviewOutcome.Refused, from), result);
        h.AssertNothingWritten();
    }

    [Fact]
    public async Task MissingRow_IsNotFound()
    {
        var h = new Harness(DecisionStatus.Review, FormState.Submitted);

        var result = await h.Service.ApproveAsync(Guid.NewGuid(), ReviewerKey, default);

        Assert.Equal(ReviewOutcome.NotFound, result.Outcome);
        h.AssertNothingWritten();
    }

    [Fact]
    public async Task MissingRecord_IsNotFound()
    {
        var h = new Harness(DecisionStatus.Review, FormState.Submitted, recordExists: false);

        var result = await h.Service.ApproveAsync(h.Row.RecordId, ReviewerKey, default);

        Assert.Equal(ReviewOutcome.NotFound, result.Outcome);
        h.AssertNothingWritten();
    }

    [Fact]
    public async Task MissingForm_IsNotFound()
    {
        var h = new Harness(DecisionStatus.Review, FormState.Submitted, formExists: false);

        var result = await h.Service.ApproveAsync(h.Row.RecordId, ReviewerKey, default);

        Assert.Equal(ReviewOutcome.NotFound, result.Outcome);
        h.AssertNothingWritten();
    }

    [Fact]
    public async Task LostRace_IsRefused_NoFormsCall()
    {
        var h = new Harness(DecisionStatus.Review, FormState.Submitted);
        h.Repo.LoseRaceTo = DecisionStatus.Quarantined;

        var result = await h.Service.ApproveAsync(h.Row.RecordId, ReviewerKey, default);

        Assert.Equal(new ReviewResult(ReviewOutcome.Refused, DecisionStatus.Quarantined), result);
        Assert.Single(h.Repo.TryReviews);
        Assert.Empty(h.Applier.Applied);
        Assert.Empty(h.Applier.Handled);
        Assert.Empty(h.Repo.Reverts);
    }

    [Fact]
    public async Task FormsThrows_RevertsAndFails()
    {
        var h = new Harness(DecisionStatus.Quarantined, FormState.Rejected, previousReviewer: "earlier-reviewer");
        h.Applier.Throw = true;

        var result = await h.Service.RestoreAsync(h.Row.RecordId, ReviewerKey, default);

        Assert.Equal(new ReviewResult(ReviewOutcome.Failed, DecisionStatus.Quarantined), result);
        var revert = Assert.Single(h.Repo.Reverts);
        Assert.Equal((DecisionStatus.Approved, DecisionStatus.Quarantined, "earlier-reviewer", FakeRepository.AuditId), revert);
        Assert.Empty(h.Applier.Handled);
    }

    private static Task<ReviewResult> Run(Harness h, ReviewAction action) => action switch
    {
        ReviewAction.Approve => h.Service.ApproveAsync(h.Row.RecordId, ReviewerKey, default),
        ReviewAction.ConfirmSpam => h.Service.ConfirmSpamAsync(h.Row.RecordId, ReviewerKey, default),
        _ => h.Service.RestoreAsync(h.Row.RecordId, ReviewerKey, default),
    };

    private sealed class Harness
    {
        public Harness(DecisionStatus status, FormState recordState, string? previousReviewer = null, bool formExists = true, bool recordExists = true)
        {
            var form = new FormsForm { Id = Guid.NewGuid(), Name = "Contact" };
            var record = new FormsRecord { UniqueId = Guid.NewGuid(), Form = form.Id, State = recordState };
            Row = new DecisionDto
            {
                Id = 1, RecordId = record.UniqueId, FormId = form.Id, Status = status.ToString(), Reviewer = previousReviewer,
            };
            Repo = new FakeRepository(Row);
            Service = new ReviewService(
                Repo,
                Fake<IFormService>.Create("Get", args => formExists && args.Length > 0 && Equals(args[0], form.Id) ? form : null),
                Fake<IRecordStorage>.Create("GetRecordByUniqueId", args => recordExists && Equals(args[0], record.UniqueId) ? record : null),
                Applier,
                NullLogger<ReviewService>.Instance);
        }

        public DecisionDto Row { get; }
        public FakeRepository Repo { get; }
        public FakeApplier Applier { get; } = new();
        public ReviewService Service { get; }

        public void AssertNothingWritten()
        {
            Assert.Empty(Repo.TryReviews);
            Assert.Empty(Repo.Reverts);
            Assert.Empty(Applier.Applied);
            Assert.Empty(Applier.Handled);
        }
    }

    /// <summary>Answers one named method; any other call fails the test.</summary>
    public class Fake<T> : DispatchProxy
        where T : class
    {
        private string _method = string.Empty;
        private Func<object?[], object?> _answer = _ => null;

        public static T Create(string method, Func<object?[], object?> answer)
        {
            var proxy = DispatchProxy.Create<T, Fake<T>>();
            var fake = (Fake<T>)(object)proxy;
            fake._method = method;
            fake._answer = answer;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == _method
                ? _answer(args ?? Array.Empty<object?>())
                : throw new NotSupportedException($"{typeof(T).Name}.{targetMethod?.Name} was not expected");
    }

    private sealed class FakeApplier : IOutcomeApplier
    {
        public bool Throw { get; set; }
        public List<DecisionStatus> Applied { get; } = new();
        public List<DecisionOutcome> Handled { get; } = new();

        public Task ApplyToRecordAsync(DecisionStatus status, FormsRecord record, FormsForm form)
        {
            if (Throw)
            {
                throw new InvalidOperationException("Forms failed");
            }

            Applied.Add(status);
            return Task.CompletedTask;
        }

        public List<CancellationToken> HandlerTokens { get; } = new();

        public Task RunHandlersAsync(DecisionOutcome outcome, CancellationToken cancellationToken)
        {
            Handled.Add(outcome);
            HandlerTokens.Add(cancellationToken);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRepository(DecisionDto row) : RepositoryStub
    {
        public const int AuditId = 42;

        /// <summary>When set, <see cref="TryReview"/> finds the row already moved to this status.</summary>
        public DecisionStatus? LoseRaceTo { get; set; }
        public List<(DecisionStatus From, DecisionStatus To, string Reviewer, string Action)> TryReviews { get; } = new();
        public List<(DecisionStatus To, DecisionStatus From, string? PreviousReviewer, int AuditId)> Reverts { get; } = new();

        public override DecisionDto? GetByRecordId(Guid recordId) => recordId == row.RecordId ? row : null;

        public override bool TryReview(DecisionDto r, DecisionStatus from, DecisionStatus to, string reviewer, string auditAction, out int auditId)
        {
            TryReviews.Add((from, to, reviewer, auditAction));
            if (LoseRaceTo is { } other)
            {
                row.Status = other.ToString();
                auditId = 0;
                return false;
            }

            r.Status = to.ToString();
            r.Reviewer = reviewer;
            auditId = AuditId;
            return true;
        }

        public override void RevertReview(DecisionDto r, DecisionStatus to, DecisionStatus from, string? previousReviewer, int auditId) =>
            Reverts.Add((to, from, previousReviewer, auditId));
    }
}
