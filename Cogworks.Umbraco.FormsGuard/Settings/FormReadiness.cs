using Cogworks.Umbraco.FormsGuard.Api.Models;
using Umbraco.Forms.Core.Enums;
using Umbraco.Forms.Core.Models;
using Umbraco.Forms.Core.Services;

namespace Cogworks.Umbraco.FormsGuard.Settings;

/// <summary>
/// Reads whether a Forms form is set up for Forms Guard to protect it: manual approval on and no active
/// workflows running on submit. Read only; the form and its workflows are never changed.
/// </summary>
public static class FormReadiness
{
    public static FormReadinessModel Build(Form form, IWorkflowService workflowService) => new()
    {
        ManualApprovalOff = !form.ManualApproval,
        SubmitWorkflowNames = (workflowService.GetActiveWorkFlows(form, FormState.Submitted) ?? [])
            .Select(w => string.IsNullOrWhiteSpace(w.Name) ? string.Empty : w.Name)
            .ToList(),
    };
}
