using Cogworks.Umbraco.FormsGuard.Decisions;
using Cogworks.Umbraco.FormsGuard.Processing;
using Cogworks.Umbraco.FormsGuard.Settings;

namespace Cogworks.Umbraco.FormsGuard.Tests.Processing;

public class QuestionMergerTests
{
    private static readonly IReadOnlyList<DecisionQuestion> Core = DefaultFormSettings.Questions
        .Select(q => new DecisionQuestion(q.Key, q.Text, q.Role))
        .ToList();

    private static DecisionQuestion Q(string key) => new(key, "text", QuestionRole.Informational);

    private static (string, Func<IEnumerable<DecisionQuestion>>) From(string source, params DecisionQuestion[] questions) =>
        (source, () => questions);

    [Fact]
    public void Merge_AppendsContributedAfterCore()
    {
        var result = QuestionMerger.Merge(Core, new[] { From("A", Q("test.deadline")) });

        Assert.Equal(6, result.Questions.Count);
        Assert.Equal(Core, result.Questions.Take(5));
        Assert.Equal("test.deadline", result.Questions[5].Key);
        Assert.Empty(result.Rejections);
    }

    [Fact]
    public void Merge_KeepsContributorRegistrationOrder()
    {
        var result = QuestionMerger.Merge(Core, new[] { From("A", Q("b.two")), From("B", Q("a.one")) });

        Assert.Equal(new[] { "b.two", "a.one" }, result.Questions.Skip(5).Select(q => q.Key));
    }

    [Fact]
    public void Merge_ReservedGuardKey_Dropped()
    {
        var result = QuestionMerger.Merge(Core, new[] { From("A", Q("guard.extra")) });

        Assert.Equal(5, result.Questions.Count);
        var rejection = Assert.Single(result.Rejections);
        Assert.Equal(("A", "guard.extra"), (rejection.Source, rejection.Key));
    }

    [Fact]
    public void Merge_DuplicateAcrossContributors_FirstWins()
    {
        var first = Q("test.deadline");
        var result = QuestionMerger.Merge(Core, new[] { From("A", first), From("B", Q("test.deadline")) });

        Assert.Same(first, result.Questions.Single(q => q.Key == "test.deadline"));
        var rejection = Assert.Single(result.Rejections);
        Assert.Equal(("B", "test.deadline"), (rejection.Source, rejection.Key));
    }

    [Fact]
    public void Merge_DuplicateOfCoreKey_Dropped()
    {
        var core = new[] { Q("x.one") };
        var result = QuestionMerger.Merge(core, new[] { From("A", Q("x.one")) });

        Assert.Single(result.Questions);
        Assert.Single(result.Rejections);
    }

    [Theory]
    [InlineData("deadline")]
    [InlineData("Test.X")]
    [InlineData("a.b.c")]
    [InlineData("")]
    [InlineData("1a.b")]
    [InlineData("test.x\n")]
    public void Merge_BadKey_Dropped(string key)
    {
        var result = QuestionMerger.Merge(Core, new[] { From("A", Q(key)) });

        Assert.Equal(5, result.Questions.Count);
        Assert.Equal(key, Assert.Single(result.Rejections).Key);
    }

    [Fact]
    public void Merge_ContributorThrows_SkipsItsQuestions_MergesRest()
    {
        IEnumerable<DecisionQuestion> Throwing()
        {
            yield return Q("bad.first");
            throw new InvalidOperationException("boom");
        }

        var result = QuestionMerger.Merge(Core, new (string, Func<IEnumerable<DecisionQuestion>>)[]
        {
            ("Thrower", Throwing),
            From("Good", Q("test.deadline")),
        });

        Assert.Equal(new[] { "test.deadline" }, result.Questions.Skip(5).Select(q => q.Key));
        var rejection = Assert.Single(result.Rejections);
        Assert.Equal("Thrower", rejection.Source);
        Assert.Null(rejection.Key);
    }

    [Fact]
    public void Merge_NoCoreNoContributors_IsEmpty()
    {
        var result = QuestionMerger.Merge(Array.Empty<DecisionQuestion>(), Array.Empty<(string, Func<IEnumerable<DecisionQuestion>>)>());

        Assert.Empty(result.Questions);
        Assert.Empty(result.Rejections);
    }
}
