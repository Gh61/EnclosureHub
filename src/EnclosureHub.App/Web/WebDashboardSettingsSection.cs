using EnclosureHub.Core.Settings;
using Microsoft.Extensions.Options;

namespace EnclosureHub.App.Web;

/// <summary>Dashboard's own settings. Authentication is deliberately file-only (Web:Auth).</summary>
internal sealed class WebDashboardSettingsSection : ISettingsSection
{
    private readonly IOptionsMonitor<WebOptions> _options;
    private readonly ISettingsStore _store;

    public WebDashboardSettingsSection(IOptionsMonitor<WebOptions> options, ISettingsStore store)
    {
        _options = options;
        _store = store;
    }

    public string Id => "dashboard";

    public string Title => "Web dashboard";

    public string? Description => "This user interface. The HTTP API and Home Assistant integration are not affected by it.";

    public IReadOnlyList<SettingField> Fields { get; } =
    [
        new("enabled", "Web dashboard", SettingFieldType.Toggle,
            "Turning it off hides this interface immediately. To bring it back, set Web:Dashboard:Enabled to true in appsettings.Local.json."),
    ];

    public IReadOnlyDictionary<string, object?> GetValues()
        => new Dictionary<string, object?> { ["enabled"] = _options.CurrentValue.Dashboard.Enabled };

    public async Task<SettingsApplyResult> ApplyAsync(IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken)
    {
        if (!values.TryGetValue("enabled", out var text))
            return new SettingsApplyResult(Message: "Nothing to save.");

        bool enabled = text?.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "on" => true,
            "false" or "0" or "off" => false,
            _ => throw new SettingsValidationException("Web dashboard must be on or off."),
        };

        await _store.SaveAsync(new Dictionary<string, object?> { ["Web:Dashboard:Enabled"] = enabled }, cancellationToken);

        return new SettingsApplyResult(Message: enabled
            ? "Saved."
            : "Saved. The dashboard is now disabled; edit appsettings.Local.json to enable it again.");
    }
}
