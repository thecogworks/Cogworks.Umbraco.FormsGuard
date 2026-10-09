using Cogworks.Umbraco.FormsGuard.Persistence;
using Microsoft.Extensions.Logging;

namespace Cogworks.Umbraco.FormsGuard.Settings;

/// <summary>Reads a form's guarded flag and effective settings.</summary>
public interface IFormSettingsReader
{
    /// <summary>No settings row means not guarded with shipped defaults.</summary>
    FormSettings Get(Guid formId);
}

public sealed class FormSettingsReader : IFormSettingsReader
{
    private readonly ISettingsRepository _repository;
    private readonly ILogger<FormSettingsReader> _logger;

    public FormSettingsReader(ISettingsRepository repository, ILogger<FormSettingsReader> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public FormSettings Get(Guid formId)
    {
        var row = _repository.GetFormSettings(formId);
        if (row is null)
        {
            return new FormSettings(false, DefaultFormSettings.Create());
        }

        var (settings, valid) = FormSettingsSerializer.Parse(row.Settings);
        if (!valid)
        {
            _logger.LogWarning(
                "FormsGuard: stored settings for form {FormId} are malformed or invalid; using shipped defaults", formId);
        }

        return new FormSettings(row.Guarded, settings);
    }
}
