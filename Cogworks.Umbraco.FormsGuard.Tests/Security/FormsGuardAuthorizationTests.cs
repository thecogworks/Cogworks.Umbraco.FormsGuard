using System.Reflection;
using Cogworks.Umbraco.FormsGuard.Api;
using Cogworks.Umbraco.FormsGuard.Api.Models;
using Cogworks.Umbraco.FormsGuard.Decisions;
using Cogworks.Umbraco.FormsGuard.Persistence;
using Cogworks.Umbraco.FormsGuard.Persistence.Dtos;
using Cogworks.Umbraco.FormsGuard.Security;
using Cogworks.Umbraco.FormsGuard.Tests.Api;
using Cogworks.Umbraco.FormsGuard.Tests.Fakes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using Umbraco.Cms.Web.Common.Authorization;
using Umbraco.Forms.Core.Models;

namespace Cogworks.Umbraco.FormsGuard.Tests.Security;

public class FormsGuardAuthorizationTests
{
    private static readonly string[] WithSection = ["content", FormsGuardPermissions.SectionAlias];
    private static readonly string[] WithoutSection = ["content"];

    private static IEnumerable<IEnumerable<string>> Groups(params string[][] groups) => groups;

    [Fact]
    public void SectionAndVerb_Passes()
    {
        Assert.True(FormsGuardAuthorizationHandler.IsAllowed(
            WithSection,
            Groups(["Umb.Document.Read"], [FormsGuardPermissions.ReviewVerb]),
            FormsGuardPermissions.ReviewVerb));
    }

    [Fact]
    public void MissingVerb_Fails()
    {
        Assert.False(FormsGuardAuthorizationHandler.IsAllowed(
            WithSection,
            Groups([FormsGuardPermissions.ManageSettingsVerb]),
            FormsGuardPermissions.ReviewVerb));
    }

    [Fact]
    public void MissingSection_Fails()
    {
        Assert.False(FormsGuardAuthorizationHandler.IsAllowed(
            WithoutSection,
            Groups([FormsGuardPermissions.ReviewVerb]),
            FormsGuardPermissions.ReviewVerb));
    }

    [Fact]
    public void NoGroups_Fails()
    {
        Assert.False(FormsGuardAuthorizationHandler.IsAllowed(WithSection, Groups(), FormsGuardPermissions.ReviewVerb));
    }

    [Theory]
    [InlineData(0, 20, 0, 20)]
    [InlineData(0, 0, 0, 1)]
    [InlineData(0, 500, 0, 100)]
    [InlineData(-1, 20, 0, 20)]
    [InlineData(40, 100, 40, 100)]
    public void Paging_IsClamped(int skip, int take, int expectedSkip, int expectedTake)
    {
        Assert.Equal((expectedSkip, expectedTake), DecisionMapping.ClampPaging(skip, take));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{not json")]
    [InlineData("[1,2]")]
    public void BadProbabilities_MapToNull(string? json)
    {
        var item = DecisionMapping.ToListItem(new DecisionDto { Id = 7, Status = "Review", Probabilities = json }, NullLogger.Instance);
        Assert.Null(item.Probabilities);
        Assert.Equal(7, item.Id);
    }

    [Fact]
    public async Task EmptyTable_ReturnsZeroTotalAndNoItems()
    {
        var controller = NewController(new PageOnlyRepository([], 0));

        var result = await controller.GetDecisions();

        Assert.Equal(0, result.Total);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Rows_KeepRepositoryOrder()
    {
        var rows = new[] { new DecisionDto { Id = 9, Status = "Review" }, new DecisionDto { Id = 3, Status = "Approved" } };
        var controller = NewController(new PageOnlyRepository(rows, 2));

        var result = await controller.GetDecisions();

        Assert.Equal(2, result.Total);
        Assert.Equal([9, 3], result.Items.Select(i => i.Id));
    }

    [Fact]
    public void Endpoint_RequiresBackOfficeAuthAndReviewPolicy()
    {
        var policies = typeof(DecisionsController)
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Concat(typeof(DecisionsController).GetMethod(nameof(DecisionsController.GetDecisions))!
                .GetCustomAttributes<AuthorizeAttribute>())
            .Select(a => a.Policy)
            .ToList();

        Assert.Contains(AuthorizationPolicies.BackOfficeAccess, policies);
        Assert.Contains(FormsGuardPermissions.ReviewPolicy, policies);
    }

    [Fact]
    public async Task Decisions_CarryNotCheckedTotalAndReviewerName()
    {
        var reviewer = Guid.NewGuid().ToString();
        var repo = new PageOnlyRepository([new DecisionDto { Id = 1, Status = "Approved", Reviewer = reviewer }], 1) { NotChecked = 3 };
        var controller = NewController(repo, new Dictionary<string, string> { [reviewer] = "Ada" });

        var result = await controller.GetDecisions(status: DecisionStatus.Review);

        Assert.Equal(3, result.ApprovedNotCheckedTotal);
        Assert.Equal("Ada", result.Items[0].ReviewerName);
        Assert.Equal(["Review"], repo.LastQuery!.Statuses);
        Assert.Equal(["ApprovedNotChecked"], repo.LastCountQuery!.Statuses);
    }

    [Fact]
    public async Task Audit_MapsActorNames()
    {
        var repo = new PageOnlyRepository([], 0)
        {
            Audit = [new AuditDto { Id = 5, Action = "decision", Actor = "system", Detail = "Approved by rule" }],
        };
        var controller = NewController(repo, new Dictionary<string, string> { ["system"] = "Forms Guard" });

        var result = await controller.GetAudit();

        Assert.Equal(1, result.Total);
        Assert.Equal("Forms Guard", result.Items[0].ActorName);
        Assert.Equal(DateTimeKind.Utc, result.Items[0].CreatedUtc.Kind);
    }

    [Theory]
    [InlineData(nameof(DecisionsController.GetDecisions))]
    [InlineData(nameof(DecisionsController.GetLogForms))]
    [InlineData(nameof(DecisionsController.GetAudit))]
    public void LogEndpoints_RequireReviewPolicy(string action)
    {
        var policies = typeof(DecisionsController).GetMethod(action)!
            .GetCustomAttributes<AuthorizeAttribute>()
            .Select(a => a.Policy);

        Assert.Contains(FormsGuardPermissions.ReviewPolicy, policies);
    }

    [Theory]
    [InlineData(typeof(DecisionListItem), new[]
    {
        "Id", "RecordId", "FormId", "Status", "Source", "RuleHit", "Provider", "ModelVersion", "Probabilities",
        "Attempts", "Reviewer", "ReviewerName", "CreatedUtc", "UpdatedUtc",
    })]
    [InlineData(typeof(AuditListItem), new[] { "Id", "RecordId", "FormId", "Action", "Actor", "ActorName", "Detail", "CreatedUtc" })]
    public void ListItems_ExposeOnlyAllowedProperties(Type type, string[] allowed)
    {
        var names = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).Order();
        Assert.Equal(allowed.Order(), names);
    }

    [Fact]
    public void LogForms_AreOrderedByName()
    {
        Form[] forms = [new Form { Id = Guid.NewGuid(), Name = "beta" }, new Form { Id = Guid.NewGuid(), Name = "Alpha" }];
        var controller = NewController(new PageOnlyRepository([], 0), forms: forms, access: FormAccessFake.AllowAll(forms.Select(f => f.Id).ToArray()));

        Assert.Equal(["Alpha", "beta"], controller.GetLogForms().Select(f => f.Name));
    }

    [Fact]
    public void LogForms_OnlyAccessibleForms()
    {
        Form[] forms = [new Form { Id = Guid.NewGuid(), Name = "A" }, new Form { Id = Guid.NewGuid(), Name = "B" }];
        var controller = NewController(new PageOnlyRepository([], 0), forms: forms, access: FormAccessFake.AllowAll(forms[0].Id));

        Assert.Equal(["A"], controller.GetLogForms().Select(f => f.Name));
    }

    [Fact]
    public async Task Log_PassesAccessibleFormsToRowsAndCounts()
    {
        var formA = Guid.NewGuid();
        var repo = new PageOnlyRepository([], 0);
        var controller = NewController(repo, access: FormAccessFake.AllowAll(formA));

        await controller.GetDecisions();
        await controller.GetAudit();

        Assert.Equal([formA], repo.LastQuery!.FormIds!);
        Assert.Equal([formA], repo.LastCountQuery!.FormIds!);
        Assert.Equal([formA], repo.LastAuditQuery!.FormIds!);
    }

    [Fact]
    public async Task Log_NoViewEntriesRight_EmptyWithoutQuery()
    {
        Form[] forms = [new Form { Id = Guid.NewGuid(), Name = "A" }];
        var repo = new PageOnlyRepository([new DecisionDto { Id = 1, Status = "Review" }], 1)
        {
            NotChecked = 4,
            Audit = [new AuditDto { Id = 5, Action = "decision", FormId = forms[0].Id }],
        };
        var controller = NewController(repo, forms: forms, access: new FormAccessFake([forms[0].Id], canViewEntries: false));

        var decisions = await controller.GetDecisions();
        var audit = await controller.GetAudit();

        Assert.Equal(0, decisions.Total);
        Assert.Equal(0, decisions.ApprovedNotCheckedTotal);
        Assert.Empty(decisions.Items);
        Assert.Equal(0, audit.Total);
        Assert.Empty(audit.Items);
        Assert.Empty(controller.GetLogForms());
        Assert.Null(repo.LastQuery);
        Assert.Null(repo.LastCountQuery);
        Assert.Null(repo.LastAuditQuery);
    }

    [Fact]
    public async Task Log_NoAccessibleForms_EmptyWithoutQuery()
    {
        var repo = new PageOnlyRepository([new DecisionDto { Id = 1, Status = "Review" }], 1);
        var controller = NewController(repo, access: FormAccessFake.AllowAll());

        Assert.Equal(0, (await controller.GetDecisions()).Total);
        Assert.Equal(0, (await controller.GetAudit()).Total);
        Assert.Null(repo.LastQuery);
        Assert.Null(repo.LastAuditQuery);
    }

    [Fact]
    public async Task Log_ExplicitFormOutsideAccess_EmptyWithoutQuery()
    {
        var repo = new PageOnlyRepository([new DecisionDto { Id = 1, Status = "Review" }], 1) { NotChecked = 2 };
        var controller = NewController(repo, access: FormAccessFake.AllowAll(Guid.NewGuid()));
        var formB = Guid.NewGuid();

        var decisions = await controller.GetDecisions(formId: formB);
        var audit = await controller.GetAudit(formId: formB);

        Assert.Equal(0, decisions.Total);
        Assert.Equal(0, decisions.ApprovedNotCheckedTotal);
        Assert.Equal(0, audit.Total);
        Assert.Null(repo.LastQuery);
        Assert.Null(repo.LastAuditQuery);
    }

    [Fact]
    public async Task Log_ExplicitFormInsideAccess_Queries()
    {
        var formA = Guid.NewGuid();
        var repo = new PageOnlyRepository([], 0);
        var controller = NewController(repo, access: FormAccessFake.AllowAll(formA, Guid.NewGuid()));

        await controller.GetDecisions(formId: formA);

        Assert.Equal(formA, repo.LastQuery!.FormId);
        Assert.Equal(formA, repo.LastCountQuery!.FormId);
    }

    private static DecisionsController NewController(
        PageOnlyRepository repo,
        IReadOnlyDictionary<string, string>? names = null,
        IReadOnlyList<Form>? forms = null,
        IFormAccess? access = null) =>
        new(repo, new FixedNames(names ?? new Dictionary<string, string>()), SettingsControllerEndpointTests.FormServiceFake.Create(forms ?? []),
            access ?? FormAccessFake.AllowAll(Guid.NewGuid()), NullLogger<DecisionsController>.Instance);

    private sealed class FixedNames(IReadOnlyDictionary<string, string> names) : IUserNameResolver
    {
        public Task<IReadOnlyDictionary<string, string>> ResolveAsync(IEnumerable<string?> actors) => Task.FromResult(names);
    }

    private sealed class PageOnlyRepository(IReadOnlyList<DecisionDto> rows, long total) : RepositoryStub
    {
        public long NotChecked { get; init; }
        public IReadOnlyList<AuditDto> Audit { get; init; } = [];
        public DecisionQuery? LastQuery { get; private set; }
        public DecisionQuery? LastCountQuery { get; private set; }
        public AuditQuery? LastAuditQuery { get; private set; }

        public override (IReadOnlyList<DecisionDto> Rows, long Total) GetPage(int skip, int take, DecisionQuery? query = null)
        {
            LastQuery = query;
            return (rows, total);
        }

        public override long CountDecisions(DecisionQuery query)
        {
            LastCountQuery = query;
            return NotChecked;
        }

        public override (IReadOnlyList<AuditDto> Rows, long Total) GetAuditPage(int skip, int take, AuditQuery? query = null)
        {
            LastAuditQuery = query;
            return (Audit, Audit.Count);
        }
    }

    [Fact]
    public void Probabilities_AreParsed()
    {
        var item = DecisionMapping.ToListItem(
            new DecisionDto { Status = "Approved", Probabilities = "{\"guard.sales_pitch\":0.1}" },
            NullLogger.Instance);
        Assert.Equal(0.1, item.Probabilities!["guard.sales_pitch"]);
        Assert.Equal(DateTimeKind.Utc, item.CreatedUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, item.UpdatedUtc.Kind);
    }

    [Theory]
    [InlineData(FormsGuardPermissions.ManageSettingsPolicy, FormsGuardPermissions.ManageSettingsVerb)]
    [InlineData(FormsGuardPermissions.ReviewPolicy, FormsGuardPermissions.ReviewVerb)]
    public void AddPolicies_EachPolicyHoldsOneRequirementWithItsVerb(string policyName, string verb)
    {
        var options = new AuthorizationOptions();
        FormsGuardApiComposer.AddPolicies(options);

        var policy = options.GetPolicy(policyName);
        Assert.NotNull(policy);
        var requirement = Assert.IsType<FormsGuardRequirement>(Assert.Single(policy!.Requirements));
        Assert.Equal(verb, requirement.Verb);
    }
}
