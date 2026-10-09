using Cogworks.Umbraco.FormsGuard.Decisions;
using Umbraco.Cms.Core.Composing;

namespace AiFormsGuard.TestSite.Extensions;

/// <summary>Registers the sample Forms Guard extensions.</summary>
public sealed class TestExtensionsComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddSingleton<IQuestionContributor, TestQuestionContributor>();
        builder.Services.AddSingleton<IOutcomeHandler, TestOutcomeHandler>();
    }
}
