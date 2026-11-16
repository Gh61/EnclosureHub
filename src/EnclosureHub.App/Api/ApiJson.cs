using System.Text.Json;
using System.Text.Json.Serialization;
using EnclosureHub.Core.Entities;

namespace EnclosureHub.App.Api;

internal static class ApiJson
{
    /// <summary>camelCase, enums as strings – shared by all API responses and the event stream.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}

internal sealed record EntityDto(
    string Id,
    string Name,
    EntityKind Kind,
    string? Icon,
    bool IsDiagnostic,
    DateTime LastChangedUtc,
    object State)
{
    public static EntityDto From(Entity entity) => new(
        entity.Id,
        entity.Name,
        entity.Kind,
        entity.Icon,
        entity.IsDiagnostic,
        entity.LastChangedUtc,
        entity.GetState());
}
