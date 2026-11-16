# Architecture

## Goals

- **Local only.** Works without internet; Home Assistant is optional, not required.
- **Modular.** Hardware features (camera, lights, fans, sensors) are independent modules that
  can be enabled in configuration. Outputs (Home Assistant, displays) don't know about hardware.
- **Light.** Must run comfortably on a Raspberry Pi 2 (1 GB RAM, 4× 900 MHz ARMv7).

## Solution layout

```
src/
  EnclosureHub.Core     Entity model, registry, camera abstraction. No dependencies.
  EnclosureHub.Camera   Camera module: rpicam-vid → latest JPEG frame.
  EnclosureHub.Mqtt     MQTT adapter: Home Assistant discovery, state, images (MQTTnet 4.x).
  EnclosureHub.App      Host: configuration, DI, HTTP API, built-in system sensors module.
tests/
  EnclosureHub.Tests    Unit tests (xUnit).
deploy/                 systemd unit, deploy scripts.
```

Planned additional projects: `EnclosureHub.Gpio` (PWM lights and fans, I²C sensors),
optionally `EnclosureHub.Display` (see ROADMAP).

## Entity model (`EnclosureHub.Core.Entities`)

Everything the controller exposes is an `Entity`:

| Class | Kind | State | Commands |
|---|---|---|---|
| `CameraEntity` | Camera | latest frame via `ICameraFrameSource` | – |
| `SensorEntity` | Sensor | `double? Value` + unit, device/state class | – |
| `BinarySensorEntity` | BinarySensor | `bool? IsOn` + device class | – |
| `LightEntity` (abstract) | Light | `LightState` (on, brightness, color temp, RGB) | `SetAsync(LightCommand)` |
| `FanEntity` (abstract) | Fan | `FanState` (on, percentage) | `SetAsync(FanCommand)` |

Rules:

- `Id` is unique per device, lowercase `snake_case`. It ends up in MQTT topics and Home Assistant unique IDs,
  so **don't change it** once deployed.
- Modules update state with `SetValue` / `SetState` / `UpdateState`; these raise `StateChanged`
  **only when the value actually changes**. The event may fire on any thread.
- New camera frames do **not** raise `StateChanged` (would flood listeners). Consumers poll
  `Source.LatestFrame` or await `Source.WaitForNextFrameAsync`.
- Commands are partial: `null` properties mean "leave unchanged".
  A hardware module applies the command, then calls `UpdateState` with the resulting state.

## Registration

Each module has an `Add…` extension method that registers its services and its entities:

```csharp
services.AddSingleton<RpicamCameraService>();
services.AddHostedService(sp => sp.GetRequiredService<RpicamCameraService>());
services.AddSingleton<Entity>(sp => sp.GetRequiredService<RpicamCameraService>().Camera);
```

`EntityRegistry` receives `IEnumerable<Entity>` from DI. The set of entities is therefore fixed
at startup, and adapters can enumerate it without synchronisation. Duplicate IDs fail at startup.

`Program.cs` enables modules and adapters based on configuration (`Modules:{Name}:Enabled`, `Mqtt:Enabled`).

## Adapters

### MQTT (`EnclosureHub.Mqtt`)

- One connection with Last Will `{base}/status = offline` (retained).
- On connect and whenever Home Assistant publishes `online` on `homeassistant/status`:
  publish discovery configs (retained), `online`, and all current states.
- `StateChanged` → queued → state published immediately (retained).
- Camera images published every `CameraImageIntervalSeconds`, only if a new frame exists.
- Mapping lives in `HomeAssistantDiscovery` – adding a new entity kind means adding a case there
  (plus command handling for controllable entities, see ROADMAP).

### HTTP API (`EnclosureHub.App/Api`)

- REST for reading entities and sending commands, MJPEG/snapshot for cameras.
- `/api/events` is a Server-Sent Events stream: one `state` event per entity on connect,
  then one per change; comment keep-alives every 15 s. This is the contract for display clients –
  a display never talks to hardware or MQTT directly.
- JSON: camelCase, enums as camelCase strings (`ApiJson.Options`).

## Adding a module (example: PWM fan)

1. Create a class deriving from `FanEntity`, implement `SetAsync` (set PWM duty, then `UpdateState`).
2. Create options + an `AddPwmFans(IConfiguration)` extension that registers the entity as `Entity`
   (and a hosted service if the module needs a background loop, e.g. tachometer reading).
3. Call it from `Program.cs` behind `Modules:Fans:Enabled`.
4. MQTT and HTTP pick the fan up automatically (MQTT once fan mapping exists – see ROADMAP).

## Resource notes for Raspberry Pi 2

- Workstation GC, no concurrent GC, invariant globalization (see `EnclosureHub.App.csproj`).
- Camera runs one long-lived `rpicam-vid` at 1 fps, software MJPEG – negligible CPU.
- Publish self-contained with ReadyToRun to cut startup time; never build on the Pi.
