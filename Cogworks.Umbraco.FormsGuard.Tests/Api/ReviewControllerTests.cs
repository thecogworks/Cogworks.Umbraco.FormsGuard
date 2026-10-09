using System.Reflection;
using Cogworks.Umbraco.FormsGuard.Api;
using Cogworks.Umbraco.FormsGuard.Api.Models;
using Cogworks.Umbraco.FormsGuard.Decisions;
using Cogworks.Umbraco.FormsGuard.Persistence;
using Cogworks.Umbraco.FormsGuard.Persistence.Dtos;
using Cogworks.Umbraco.FormsGuard.Review;
using Cogworks.Umbraco.FormsGuard.Security;
using Cogworks.Umbraco.FormsGuard.Tests.Fakes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Security;
using Umbraco.Forms.Core.Data.Storage;
using Umbraco.Forms.Core.Services;
using FormsForm = Umbraco.Forms.Core.Models.Form;

namespace Cogworks.Umbraco.FormsGuard.Tests.Api;

public class ReviewControllerTests
{
    [Fact]
    public void Done_Is200WithStatus()
    {
        var result = Assert.IsType<ObjectResult>(
            ReviewController.ToActionResult(new ReviewResult(ReviewOutcome.Done, DecisionStatus.Approved)));
        Assert.Equal(200, result.StatusCode);
        var body = Assert.IsType<ReviewActionResponse>(result.Value);
        Assert.Equal("Done", body.Outcome);
        Assert.Equal("Approved", body.Status);
    }

    [Fact]
    public void NotFound_Is404()
    {
        Assert.IsType<NotFoundResult>(ReviewController.ToActionResult(new ReviewResult(ReviewOutcome.NotFound, null)));
    }

    [Fact]
    public void Refused_Is409WithStatus()
    {
        var result = Assert.IsType<ObjectResult>(
            ReviewController.ToActionResult(new ReviewResult(ReviewOutcome.Refused, DecisionStatus.Quarantined)));
        Assert.Equal(409, result.StatusCode);
        var body = Assert.IsType<ReviewActionResponse>(result.Value);
        Assert.Equal("Refused", body.Outcome);
        Assert.Equal("Quarantined", body.Status);
    }

    [Fact]
    public void Failed_Is500()
    {
        var result = Assert.IsType<ObjectResult>(
            ReviewController.ToActionResult(new ReviewResult(ReviewOutcome.Failed, null)));
        Assert.Equal(500, result.StatusCode);
        var body = Assert.IsType<ReviewActionResponse>(result.Value);
        Assert.Equal("Failed", body.Outcome);
        Assert.Null(body.Status);
    }

    [Theory]
    [InlineData(nameof(ReviewController.GetReviewQueue))]
    [InlineData(nameof(ReviewController.Approve))]
    [InlineData(nameof(ReviewController.ConfirmSpam))]
    [InlineData(nameof(ReviewController.Restore))]
    public void Endpoints_RequireReviewPolicy(string action)
    {
        var policies = typeof(ReviewController).GetMethod(action)!
            .GetCustomAttributes<AuthorizeAttribute>()
            .Select(a => a.Policy);
        Assert.Contains(FormsGuardPermissions.ReviewPolicy, policies);
    }

    [Fact]
    public void ReviewQueue_LoadsEachFormOnce()
    {
        var form = new FormsForm { Id = Guid.NewGuid(), Name = "Contact" };
        var forms = CountingFormService.Create(form);
        var records = NoRecordStorage.Create();

        var result = NewController(QueueRows(form.Id, 3), forms, records, FormAccessFake.AllowAll(form.Id)).GetReviewQueue();

        Assert.Equal(1, ((CountingFormService)(object)forms).Gets[form.Id]);
        Assert.Equal(3, ((NoRecordStorage)(object)records).Lookups);
        Assert.All(result.Items, item =>
        {
            Assert.True(item.RecordMissing);
            Assert.Equal("Contact", item.FormName);
        });
    }

    [Fact]
    public void ReviewQueue_DeletedForm_EveryRowRecordMissing()
    {
        var formId = Guid.NewGuid();
        var forms = CountingFormService.Create();

        var result = NewController(QueueRows(formId, 3), forms, NoRecordStorage.Create(), FormAccessFake.AllowAll(formId))
            .GetReviewQueue();

        Assert.Equal(1, ((CountingFormService)(object)forms).Gets[formId]);
        Assert.Equal(3, result.Items.Count);
        Assert.All(result.Items, item =>
        {
            Assert.True(item.RecordMissing);
            Assert.Null(item.FormName);
        });
    }

    [Fact]
    public void ReviewQueue_PassesOnlyAccessibleForms()
    {
        var formA = Guid.NewGuid();
        var repo = new QueueRepository([]);

        var result = NewController(repo, CountingFormService.Create(), NoRecordStorage.Create(), FormAccessFake.AllowAll(formA))
            .GetReviewQueue();

        Assert.Equal([formA], repo.LastQuery!.FormIds!);
        Assert.Equal(ReviewController.QueueStatuses, repo.LastQuery.Statuses);
        Assert.Equal(0, result.Total);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ReviewQueue_ReportsEditEntriesRight(bool canView, bool canEdit)
    {
        var formA = Guid.NewGuid();
        var access = new FormAccessFake([formA], canViewEntries: canView, canEditEntries: canEdit);

        var result = NewController(new QueueRepository([]), CountingFormService.Create(), NoRecordStorage.Create(), access)
            .GetReviewQueue();

        Assert.Equal(canEdit, result.CanEditEntries);
    }

    [Fact]
    public void ReviewQueue_NoViewEntriesRight_EmptyWithoutQuery()
    {
        var repo = new QueueRepository(QueueRows(Guid.NewGuid(), 2));
        var access = new FormAccessFake([Guid.NewGuid()], canViewEntries: false);

        var result = NewController(repo, CountingFormService.Create(), NoRecordStorage.Create(), access).GetReviewQueue();

        Assert.Equal(0, result.Total);
        Assert.Empty(result.Items);
        Assert.Null(repo.LastQuery);
    }

    [Fact]
    public void ReviewQueue_NoAccessibleForms_EmptyWithoutQuery()
    {
        var repo = new QueueRepository(QueueRows(Guid.NewGuid(), 2));

        var result = NewController(repo, CountingFormService.Create(), NoRecordStorage.Create(), FormAccessFake.AllowAll())
            .GetReviewQueue();

        Assert.Equal(0, result.Total);
        Assert.Null(repo.LastQuery);
    }

    public static TheoryData<string> Actions => [nameof(ReviewController.Approve), nameof(ReviewController.ConfirmSpam), nameof(ReviewController.Restore)];

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task Action_FormNotAccessible_Is404_ServiceNotCalled(string action)
    {
        var (row, repo) = SingleRow(Guid.NewGuid());
        var service = new RecordingReviewService();

        var result = await Run(action, NewActionController(repo, service, FormAccessFake.AllowAll(Guid.NewGuid())), row.RecordId);

        Assert.IsType<NotFoundResult>(result);
        Assert.Empty(service.Calls);
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task Action_NoViewEntriesRight_Is404_ServiceNotCalled(string action)
    {
        var (row, repo) = SingleRow(Guid.NewGuid());
        var service = new RecordingReviewService();
        var access = new FormAccessFake([row.FormId], canViewEntries: false);

        Assert.IsType<NotFoundResult>(await Run(action, NewActionController(repo, service, access), row.RecordId));
        Assert.Empty(service.Calls);
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task Action_AccessibleWithoutEditRight_Is403_ServiceNotCalled(string action)
    {
        var (row, repo) = SingleRow(Guid.NewGuid());
        var service = new RecordingReviewService();
        var access = new FormAccessFake([row.FormId], canEditEntries: false);

        var result = Assert.IsType<StatusCodeResult>(await Run(action, NewActionController(repo, service, access), row.RecordId));

        Assert.Equal(403, result.StatusCode);
        Assert.Empty(service.Calls);
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task Action_AccessibleWithEditRight_CallsService(string action)
    {
        var (row, repo) = SingleRow(Guid.NewGuid());
        var service = new RecordingReviewService();

        var result = Assert.IsType<ObjectResult>(
            await Run(action, NewActionController(repo, service, FormAccessFake.AllowAll(row.FormId)), row.RecordId));

        Assert.Equal(200, result.StatusCode);
        Assert.Equal([(action, row.RecordId, ReviewerKey)], service.Calls);
    }

    [Fact]
    public async Task Action_MissingRow_Is404_ServiceNotCalled()
    {
        var service = new RecordingReviewService();
        var controller = NewActionController(new QueueRepository([]), service, FormAccessFake.AllowAll(Guid.NewGuid()));

        Assert.IsType<NotFoundResult>(await controller.Approve(Guid.NewGuid(), CancellationToken.None));
        Assert.Empty(service.Calls);
    }

    private static readonly Guid ReviewerKey = Guid.NewGuid();

    private static (DecisionDto Row, QueueRepository Repo) SingleRow(Guid formId)
    {
        var row = QueueRows(formId, 1)[0];
        return (row, new QueueRepository([row]));
    }

    private static Task<IActionResult> Run(string action, ReviewController controller, Guid recordId) => action switch
    {
        nameof(ReviewController.Approve) => controller.Approve(recordId, CancellationToken.None),
        nameof(ReviewController.ConfirmSpam) => controller.ConfirmSpam(recordId, CancellationToken.None),
        _ => controller.Restore(recordId, CancellationToken.None),
    };

    private static ReviewController NewActionController(QueueRepository repo, IReviewService service, IFormAccess access) =>
        new(repo, service, CountingFormService.Create(), NoRecordStorage.Create(), SignedIn.Create(ReviewerKey), access,
            NullLogger<ReviewController>.Instance);

    private static IReadOnlyList<DecisionDto> QueueRows(Guid formId, int count) =>
        Enumerable.Range(1, count)
            .Select(i => new DecisionDto { Id = i, RecordId = Guid.NewGuid(), FormId = formId, Status = "Review" })
            .ToList();

    private static ReviewController NewController(
        IReadOnlyList<DecisionDto> rows, IFormService forms, IRecordStorage records, IFormAccess access) =>
        NewController(new QueueRepository(rows), forms, records, access);

    private static ReviewController NewController(QueueRepository repo, IFormService forms, IRecordStorage records, IFormAccess access) =>
        new(repo, Throwing<IReviewService>.Create(), forms, records,
            Throwing<IBackOfficeSecurityAccessor>.Create(), access, NullLogger<ReviewController>.Instance);

    private sealed class QueueRepository(IReadOnlyList<DecisionDto> rows) : RepositoryStub
    {
        public DecisionQuery? LastQuery { get; private set; }

        public override (IReadOnlyList<DecisionDto> Rows, long Total) GetPage(int skip, int take, DecisionQuery? query = null)
        {
            LastQuery = query;
            return (rows, rows.Count);
        }

        public override DecisionDto? GetByRecordId(Guid recordId) => rows.FirstOrDefault(r => r.RecordId == recordId);
    }

    private sealed class RecordingReviewService : IReviewService
    {
        public List<(string Action, Guid RecordId, Guid Reviewer)> Calls { get; } = [];

        public Task<ReviewResult> ApproveAsync(Guid recordId, Guid reviewerKey, CancellationToken cancellationToken) =>
            Record(nameof(ReviewController.Approve), recordId, reviewerKey);

        public Task<ReviewResult> ConfirmSpamAsync(Guid recordId, Guid reviewerKey, CancellationToken cancellationToken) =>
            Record(nameof(ReviewController.ConfirmSpam), recordId, reviewerKey);

        public Task<ReviewResult> RestoreAsync(Guid recordId, Guid reviewerKey, CancellationToken cancellationToken) =>
            Record(nameof(ReviewController.Restore), recordId, reviewerKey);

        private Task<ReviewResult> Record(string action, Guid recordId, Guid reviewerKey)
        {
            Calls.Add((action, recordId, reviewerKey));
            return Task.FromResult(new ReviewResult(ReviewOutcome.Done, DecisionStatus.Approved));
        }
    }

    /// <summary>Counts <c>Get(Guid)</c> calls per form id; anything else is unexpected.</summary>
    public class CountingFormService : DispatchProxy
    {
        private FormsForm[] _forms = [];

        public Dictionary<Guid, int> Gets { get; } = new();

        public static IFormService Create(params FormsForm[] forms)
        {
            var proxy = DispatchProxy.Create<IFormService, CountingFormService>();
            ((CountingFormService)(object)proxy)._forms = forms;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var parameters = targetMethod?.GetParameters() ?? [];
            if (targetMethod?.Name == "Get" && parameters.Length == 1 && parameters[0].ParameterType == typeof(Guid))
            {
                var id = (Guid)args![0]!;
                Gets[id] = Gets.GetValueOrDefault(id) + 1;
                return _forms.FirstOrDefault(f => f.Id == id);
            }

            throw new NotSupportedException($"IFormService.{targetMethod?.Name} was not expected");
        }
    }

    /// <summary>Finds no record; anything other than <c>GetRecordByUniqueId</c> is unexpected.</summary>
    public class NoRecordStorage : DispatchProxy
    {
        public int Lookups { get; private set; }

        public static IRecordStorage Create() => DispatchProxy.Create<IRecordStorage, NoRecordStorage>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "GetRecordByUniqueId")
            {
                Lookups++;
                return null;
            }

            throw new NotSupportedException($"IRecordStorage.{targetMethod?.Name} was not expected");
        }
    }

    /// <summary>Throws on every call.</summary>
    public class Throwing<T> : DispatchProxy where T : class
    {
        public static T Create() => DispatchProxy.Create<T, Throwing<T>>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException($"{typeof(T).Name}.{targetMethod?.Name} was not expected");
    }
}
