using Cogworks.Umbraco.FormsGuard.Decisions;

namespace AiFormsGuard.TestSite.Extensions;

/// <summary>Sample contributor: asks whether the entry mentions a deadline, for every form.</summary>
public sealed class TestQuestionContributor : IQuestionContributor
{
    public IEnumerable<DecisionQuestion> GetQuestions(Guid formId)
    {
        yield return new DecisionQuestion(
            "test.deadline",
            "Does the message mention a deadline or a date by which the sender needs a reply?",
            QuestionRole.Informational,
            QuestionType.Noul,
            TrueCriteria: "The message mentions a deadline or a date.",
            FalseCriteria: "The message mentions no deadline or date.");
    }
}
