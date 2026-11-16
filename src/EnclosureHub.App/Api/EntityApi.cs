using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using EnclosureHub.Core;
using EnclosureHub.Core.Entities;

namespace EnclosureHub.App.Api;

/// <summary>
/// Local HTTP API used by displays and other clients:
///   GET  /api/entities              – all entities with current state
///   GET  /api/entities/{id}         – one entity
///   POST /api/entities/{id}/command – LightCommand / FanCommand as JSON
///   GET  /api/events                – Server-Sent Events stream of state changes
/// </summary>
internal static class EntityApi
{
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(15);

    public static IEndpointRouteBuilder MapEntityApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/entities");

        group.MapGet("/", (IEntityRegistry registry) =>
            Results.Json(registry.All.Select(EntityDto.From), ApiJson.Options));

        group.MapGet("/{id}", (string id, IEntityRegistry registry) =>
            registry.Find(id) is { } entity
                ? Results.Json(EntityDto.From(entity), ApiJson.Options)
                : Results.NotFound());

        group.MapPost("/{id}/command", HandleCommandAsync);

        app.MapGet("/api/events", StreamEventsAsync);

        return app;
    }

    private static async Task<IResult> HandleCommandAsync(
        string id, JsonElement body, IEntityRegistry registry, CancellationToken cancellationToken)
    {
        var entity = registry.Find(id);
        if (entity == null)
            return Results.NotFound();

        try
        {
            switch (entity)
            {
                case LightEntity light:
                    var lightCommand = body.Deserialize<LightCommand>(ApiJson.Options);
                    if (lightCommand == null)
                        return Results.BadRequest(new { error = "Empty command." });
                    await light.SetAsync(lightCommand, cancellationToken);
                    break;

                case FanEntity fan:
                    var fanCommand = body.Deserialize<FanCommand>(ApiJson.Options);
                    if (fanCommand == null)
                        return Results.BadRequest(new { error = "Empty command." });
                    await fan.SetAsync(fanCommand, cancellationToken);
                    break;

                default:
                    return Results.BadRequest(new { error = $"Entity '{id}' ({entity.Kind}) does not accept commands." });
            }
        }
        catch (JsonException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }

        return Results.Json(EntityDto.From(entity), ApiJson.Options);
    }

    /// <summary>
    /// Server-Sent Events: sends a "state" event for every entity on connect,
    /// then one per state change. Comment lines keep the connection alive.
    /// </summary>
    private static async Task StreamEventsAsync(HttpContext context, IEntityRegistry registry)
    {
        var cancellationToken = context.RequestAborted;

        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";

        var changed = Channel.CreateBounded<Entity>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
        });

        EventHandler handler = (sender, _) =>
        {
            if (sender is Entity entity)
                changed.Writer.TryWrite(entity);
        };

        foreach (var entity in registry.All)
            entity.StateChanged += handler;

        try
        {
            foreach (var entity in registry.All)
                await WriteStateEventAsync(context.Response, entity, cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                using var keepAlive = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                keepAlive.CancelAfter(KeepAliveInterval);

                bool hasData;
                try
                {
                    hasData = await changed.Reader.WaitToReadAsync(keepAlive.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    await WriteRawAsync(context.Response, ": keep-alive\n\n", cancellationToken);
                    continue;
                }

                if (!hasData)
                    break;

                while (changed.Reader.TryRead(out var entity))
                    await WriteStateEventAsync(context.Response, entity, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // client disconnected
        }
        finally
        {
            foreach (var entity in registry.All)
                entity.StateChanged -= handler;
        }
    }

    private static Task WriteStateEventAsync(HttpResponse response, Entity entity, CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(EntityDto.From(entity), ApiJson.Options);
        return WriteRawAsync(response, "event: state\ndata: " + json + "\n\n", cancellationToken);
    }

    private static async Task WriteRawAsync(HttpResponse response, string text, CancellationToken cancellationToken)
    {
        await response.Body.WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}
