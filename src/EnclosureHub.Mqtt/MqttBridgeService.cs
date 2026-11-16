using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using EnclosureHub.Core;
using EnclosureHub.Core.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace EnclosureHub.Mqtt;

/// <summary>
/// Publishes all registered entities to an MQTT broker using Home Assistant MQTT discovery.
/// Keeps the connection alive, republishes discovery when Home Assistant restarts,
/// pushes state changes immediately and camera images periodically.
/// </summary>
public sealed class MqttBridgeService : BackgroundService
{
    private static readonly TimeSpan IdleWakeup = TimeSpan.FromSeconds(5);

    private readonly MqttBridgeOptions _options;
    private readonly DeviceOptions _device;
    private readonly IEntityRegistry _registry;
    private readonly ILogger<MqttBridgeService> _logger;
    private readonly IMqttClient _client;
    private readonly MqttTopics _topics;
    private readonly Channel<Entity> _changed = Channel.CreateUnbounded<Entity>();

    private volatile bool _discoveryPending = true;

    public MqttBridgeService(
        IOptions<MqttBridgeOptions> options,
        IOptions<DeviceOptions> device,
        IEntityRegistry registry,
        ILogger<MqttBridgeService> logger)
    {
        _options = options.Value;
        _device = device.Value;
        _registry = registry;
        _logger = logger;

        string baseTopic = string.IsNullOrWhiteSpace(_options.BaseTopic) ? _device.Id : _options.BaseTopic;
        _topics = new MqttTopics(baseTopic, _options.DiscoveryPrefix, _device.Id);

        _client = new MqttFactory().CreateMqttClient();
        _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
        _client.DisconnectedAsync += e =>
        {
            if (e.ClientWasConnected)
                _logger.LogWarning("MQTT disconnected: {Reason}", e.ReasonString ?? e.Reason.ToString());
            return Task.CompletedTask;
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Host))
        {
            _logger.LogInformation("MQTT bridge disabled (Mqtt:Host is empty)");
            return;
        }

        foreach (var entity in _registry.All)
            entity.StateChanged += OnEntityStateChanged;

        var cameraTask = PublishCameraImagesAsync(stoppingToken);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!_client.IsConnected)
                    {
                        await ConnectAsync(stoppingToken);
                        _discoveryPending = true;
                    }

                    if (_discoveryPending)
                    {
                        _discoveryPending = false;
                        await PublishDiscoveryAsync(stoppingToken);
                        await PublishAsync(_topics.Availability, "online", retain: true, stoppingToken);
                        foreach (var entity in _registry.All)
                            await PublishStateAsync(entity, stoppingToken);
                    }

                    await PublishNextChangeAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("MQTT error: {Message}. Retrying in {Delay} s", ex.Message, _options.ReconnectDelaySeconds);
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(_options.ReconnectDelaySeconds), stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }
        finally
        {
            foreach (var entity in _registry.All)
                entity.StateChanged -= OnEntityStateChanged;

            await cameraTask;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_client.IsConnected)
        {
            try
            {
                await PublishAsync(_topics.Availability, "offline", retain: true, cancellationToken);
                await _client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build(), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error while disconnecting from MQTT");
            }
        }

        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _client.Dispose();
        base.Dispose();
    }

    private void OnEntityStateChanged(object? sender, EventArgs e)
    {
        if (sender is Entity entity)
            _changed.Writer.TryWrite(entity);
    }

    /// <summary>Waits for the next changed entity (or a short timeout so the loop can check the connection).</summary>
    private async Task PublishNextChangeAsync(CancellationToken stoppingToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(IdleWakeup);

        Entity entity;
        try
        {
            entity = await _changed.Reader.ReadAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            return; // idle timeout
        }

        if (_client.IsConnected)
            await PublishStateAsync(entity, stoppingToken);
    }

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        string clientId = string.IsNullOrWhiteSpace(_options.ClientId) ? _device.Id : _options.ClientId;

        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(_options.Host, _options.Port)
            .WithClientId(clientId)
            .WithCleanSession()
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            .WithWillTopic(_topics.Availability)
            .WithWillPayload("offline")
            .WithWillRetain(true)
            .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce);

        if (!string.IsNullOrEmpty(_options.Username))
            builder = builder.WithCredentials(_options.Username, _options.Password);

        _logger.LogInformation("Connecting to MQTT broker {Host}:{Port}", _options.Host, _options.Port);
        await _client.ConnectAsync(builder.Build(), cancellationToken);

        await _client.SubscribeAsync(
            new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(_topics.HomeAssistantStatus)
                .Build(),
            cancellationToken);

        _logger.LogInformation("Connected to MQTT broker");
    }

    private Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        if (e.ApplicationMessage.Topic == _topics.HomeAssistantStatus
            && e.ApplicationMessage.ConvertPayloadToString() == "online")
        {
            _logger.LogInformation("Home Assistant came online, republishing discovery");
            _discoveryPending = true;
        }

        // Command topics ({base}/{entity}/set) for lights and fans: see docs/ROADMAP.md
        return Task.CompletedTask;
    }

    private async Task PublishDiscoveryAsync(CancellationToken cancellationToken)
    {
        foreach (var entity in _registry.All)
        {
            string? component = HomeAssistantDiscovery.GetComponent(entity);
            if (component == null)
            {
                _logger.LogWarning("Entity {Id} ({Kind}) is not supported by the MQTT bridge yet", entity.Id, entity.Kind);
                continue;
            }

            var config = HomeAssistantDiscovery.BuildConfig(entity, _topics, _device);
            await PublishAsync(_topics.DiscoveryConfig(component, entity.Id), JsonSerializer.Serialize(config), retain: true, cancellationToken);
        }

        _logger.LogInformation("Published Home Assistant discovery for {Count} entities", _registry.All.Count);
    }

    private Task PublishStateAsync(Entity entity, CancellationToken cancellationToken)
    {
        string? payload = HomeAssistantDiscovery.FormatState(entity);
        return payload == null
            ? Task.CompletedTask
            : PublishAsync(_topics.State(entity.Id), payload, retain: true, cancellationToken);
    }

    private async Task PublishCameraImagesAsync(CancellationToken stoppingToken)
    {
        var cameras = _registry.All.OfType<CameraEntity>().ToList();
        if (cameras.Count == 0)
            return;

        var lastSent = new Dictionary<string, DateTime>();
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.CameraImageIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (!_client.IsConnected)
                    continue;

                foreach (var camera in cameras)
                {
                    var frame = camera.Source.LatestFrame;
                    if (frame == null
                        || (lastSent.TryGetValue(camera.Id, out var sent) && frame.TimestampUtc <= sent))
                        continue; // nothing new

                    try
                    {
                        await PublishAsync(_topics.Image(camera.Id), frame.Jpeg, retain: false, stoppingToken);
                        lastSent[camera.Id] = frame.TimestampUtc;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogDebug("Failed to publish image for {Id}: {Message}", camera.Id, ex.Message);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    private Task PublishAsync(string topic, string payload, bool retain, CancellationToken cancellationToken)
        => PublishAsync(topic, Encoding.UTF8.GetBytes(payload), retain, cancellationToken);

    private Task PublishAsync(string topic, byte[] payload, bool retain, CancellationToken cancellationToken)
    {
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithRetainFlag(retain)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
            .Build();

        return _client.PublishAsync(message, cancellationToken);
    }
}
