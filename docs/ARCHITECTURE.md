# Architecture

## Goals

- **Local only.** Works without internet; Home Assistant is optional, not required.
- **Modular.** Hardware features (camera, lights, fans, sensors) are independent modules that
  can be enabled in configuration. Outputs (Home Assistant, displays) don't know about hardware.
- **Light.** Must run comfortably on a Raspberry Pi 2 (1 GB RAM, 4× 900 MHz ARMv7).

## Solution layout

```
src/
  EnclosureHub.Core     Entity model, registry, camera abstraction, settings abstraction. No dependencies.
  EnclosureHub.Camera   Camera module: rpicam-vid → latest JPEG frame; camera settings section.
  EnclosureHub.Mqtt     MQTT adapter: Home Assistant discovery, state, images (MQTTnet 4.x).
  EnclosureHub.App      Host: configuration, DI, HTTP API, web dashboard (wwwroot), Basic auth,
                        settings store, built-in system sensors module, --install/--uninstall.
tests/
  EnclosureHub.Tests    Unit tests (xUnit).
deploy/                 Deploy scripts (publish + copy over SSH).
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
| `SwitchEntity` (abstract) | Switch | `bool? IsOn` | `SetAsync(bool)` |
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

`Program.cs` enables modules and adapters based on configuration (`Modules:{Name}:Enabled`, `Mqtt:Enabled`);
a module's `Add…` method checks its own `Enabled` flag. `Enabled` means "module present" and is file-only.
Runtime on/off states that must survive restarts and stay controllable (camera `Active`) are separate
options, exposed as switch entities.

## Runtime settings (`EnclosureHub.Core.Settings`)

Modules can expose settings for editing at runtime without the UI knowing anything about hardware:

- `ISettingsSection` – id, title, a list of `SettingField`s (toggle / number / text / choice), `GetValues()`
  and `ApplyAsync(values)`. The module validates (`SettingsValidationException` → HTTP 400), persists
  and applies the change. The camera section restarts rpicam-vid through `IOptionsMonitor`.
- `ISettingsStore` – persists `"Config:Path" = value` pairs. The App implementation merges them into
  `appsettings.Local.json` (atomic replace, file mode preserved) and calls `IConfigurationRoot.Reload()`,
  which fires the option monitors.

`GET/PUT /api/settings` exposes all sections; the dashboard renders them generically. Settings that are
secrets or rarely change (MQTT, `Web:Auth`) are deliberately file-only.

## Adapters

### MQTT (`EnclosureHub.Mqtt`)

- One connection with Last Will `{base}/status = offline` (retained).
- On connect and whenever Home Assistant publishes `online` on `homeassistant/status`:
  publish discovery configs (retained), `online`, and all current states.
- `StateChanged` → queued → state published immediately (retained).
- Subscribed to `{base}/+/set`; `ON`/`OFF` on a switch's command topic calls `SetAsync`. The state is
  published only when the module reports the new state (no optimistic update), so HA shows reality.
  The camera module's `camera_active` switch persists `Modules:Camera:Active` and the service reacts via
  its options monitor – the same path the dashboard and the HTTP command use.
- Camera: every `CameraImageIntervalSeconds` (new frames only) either the JPEG is published
  (`CameraMode: Image`, HA component `camera`) or a snapshot URL with a cache-busting query
  (`CameraMode: Link`, HA component `image` with `url_topic`). Switching the mode republishes discovery
  and clears the other component's retained config. Stream/snapshot/dashboard URLs go to
  `{base}/{id}/attributes` in both modes; the base URL is `Mqtt:PublicUrl` or the detected LAN address.
- Connection settings are read once; camera mode, interval and public URL come from `IOptionsMonitor`
  and apply live (the dashboard's "Home Assistant" settings section).
- Mapping lives in `HomeAssistantDiscovery` – adding a new entity kind means adding a case there
  (plus command handling for controllable entities, see ROADMAP).

### HTTP API (`EnclosureHub.App/Api`)

- REST for reading entities and sending commands, MJPEG/snapshot for cameras.
- `/api/events` is a Server-Sent Events stream: one `state` event per entity on connect,
  then one per change; comment keep-alives every 15 s. This is the contract for display clients –
  a display never talks to hardware or MQTT directly.
- JSON: camelCase, enums as camelCase strings (`ApiJson.Options`).
- `Web:Auth` (Basic authentication) wraps everything except `/health`; `Web:Dashboard:Enabled` only gates
  the static files. Both are read per request via `IOptionsMonitor`, so a settings save applies immediately.

### Web dashboard (`EnclosureHub.App/wwwroot`)

Static `index.html` + `dashboard.css` + `dashboard.js`, no build step, no framework. It is a client of the
HTTP API only (entities, SSE, camera endpoints, settings, system) and therefore also the reference
implementation for other display clients. The look is deliberately minimal: black background, white
text, outlined white controls, thin separators, a small green/red dot as the only accent. The MJPEG
stream is attached only while the Camera page is visible to spare the Pi.

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
