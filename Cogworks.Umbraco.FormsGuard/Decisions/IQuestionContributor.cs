namespace Cogworks.Umbraco.FormsGuard.Decisions;

/// <summary>
/// Adds questions to the per-entry provider request. Question keys must be namespaced
/// (e.g. <c>triage.team</c>); the <c>guard.*</c> namespace is reserved for the core package.
/// </summary>
public interface IQuestionContributor
{
    /// <summary>Questions to add for entries of the given form; empty if none apply.</summary>
    IEnumerable<DecisionQuestion> GetQuestions(Guid formId);
}
