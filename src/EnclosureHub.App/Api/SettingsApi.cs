using System.Text.Json;
using EnclosureHub.Core.Settings;

namespace EnclosureHub.App.Api;

/// <summary>
///   GET /api/settings        – all settings sections with fields and current values
///   PUT /api/settings/{id}   – apply values for one section; body: {"key": value, …}
/// </summary>
internal static class SettingsApi
{
    public static IEndpointRouteBuilder MapSettingsApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings");

        group.MapGet("/", (IEnumerable<ISettingsSection> sections) =>
            Results.Json(sections.Select(SectionDto.From), ApiJson.Options));

        group.MapPut("/{id}", async (string id, Dictionary<string, JsonElement> body, IEnumerable<ISettingsSection> sections, CancellationToken cancellationToken) =>
        {
            var section = sections.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
            if (section == null)
                return Results.NotFound();

            var values = body.ToDictionary(p => p.Key, p => ToText(p.Value), StringComparer.OrdinalIgnoreCase);

            try
            {
                var result = await section.ApplyAsync(values, cancellationToken);
                return Results.Json(new
                {
                    result.RestartRequired,
                    result.Message,
                    Values = section.GetValues(),
                }, ApiJson.Options);
            }
            catch (SettingsValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Results.Json(new { error = "Could not write the configuration file: " + ex.Message }, ApiJson.Options,
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        return app;
    }

    private static string? ToText(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.String => element.GetString(),
        _ => element.GetRawText(),
    };

    private sealed record SectionDto(
        string Id,
        string Title,
        string? Description,
        string Group,
        IReadOnlyList<SettingField> Fields,
        IReadOnlyDictionary<string, object?> Values)
    {
        public static SectionDto From(ISettingsSection section)
            => new(section.Id, section.Title, section.Description, section.Group, section.Fields, section.GetValues());
    }
}
