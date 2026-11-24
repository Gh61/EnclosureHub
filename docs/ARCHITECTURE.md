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
  EnclosureHub.Printer  Printer module: Prusa (Buddy firmware) over USB serial – print/layer events, entities.
  EnclosureHub.Timelapse Timelapse module: saves a camera entity's latest frame periodically, renders with ffmpeg.
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

`GET/PUT /api/settings` exposes all sections; the dashboard renders them generically, grouped into tabs by
`ISettingsSection.Group` (`SettingsGroups`: Devices, Printer, Timelapse, Integrations; tabs in order of first use).
Besides value fields there are `Heading` (a sub-heading; the following fields belong to it) and `Info` (read-only
text). `SettingField.Tag` marks related fields across sections: fields tagged `printer` exist only while the
printer is linked (the sections ask `IPrinterConnection.IsLinked`), and the printer section lists them with their
values. Saving the printer section re-renders all settings (fields appear/disappear).
A section may get small dashboard-side extras keyed by its id (`sectionExtras` in `dashboard.js`; the
timelapse section adds the ffmpeg status and live storage estimates). Settings that are secrets or rarely
change (MQTT, `Web:Auth`) are deliberately file-only.

## Printer connection (`EnclosureHub.Printer`)

`PrusaUsbPrinter` implements `IPrinterConnection` (Core): other modules subscribe to `EventRaised`
(`PrintStarted`, `PrintFinished`, `LayerChanged`) and read `GetStatus()`; none of them references the printer module.

- Transport: the printer's USB-C port is a CDC serial device; `System.IO.Ports.SerialPort` with a 500 ms read
  timeout on a dedicated thread (blocking reads are how an unplugged printer is noticed). Port = `Modules:Printer:Port`
  or the first `/dev/serial/by-id/*Prusa*`. Reconnects every `ReconnectSeconds` while linked; idle while not linked.
- Linking: the settings section calls `ReadInfoAsync` (opens the port briefly, `M115`) and saves `Linked = true`
  plus model/firmware/UUID only if the printer answered. Afterwards the hub works without the printer.
- `PrusaStateTracker` turns lines into state and events (see its comment for the observed protocol). Layer = Z
  increase of ≤ 1.5 mm while printing; a decrease restarts at layer 1 (first layer after the purge position).
  `Done printing file` ends a print (finished and cancelled, per `finalize_print` in the firmware); `Not SD printing`
  does not (also reported while paused); losing the port does.
- Writes only `M155 S{n}` / `M27` (when `Query`) and `M115` on request; `M155 S0` once when queries are switched off
  or the printer is unlinked. `M27` goes out only when the previous query was answered (`ok`) and no `echo:busy`
  came within 5 s – during blocking commands the firmware does not read its input, and piled-up queries made the
  2 s write timeout fire (which once dropped the connection and, with it, the timelapse). A write timeout is now
  just logged. A ring buffer keeps the last 500 lines for the Printer page.
- Lines are assembled from raw bytes (`SerialPort.Read`), not `ReadLine`, so a read timeout never cuts a line.
- A lost port during a print starts a 60 s grace period; after a reconnect the first `M27` answer decides
  (`SD printing` = continue, `Not SD printing` = finished). Unlinking ends it immediately.
- Entities exist only if the printer was linked and `Entities` on at startup (the entity set is fixed).

Consumers: the timelapse (layer frames, follow the print) and the camera (layer frames). Planned: light/fan rules.

## Timelapse (`EnclosureHub.Timelapse`)

A module that consumes another module's entity instead of hardware: it finds the camera entity
(`Modules:Timelapse:CameraId`) in the registry and, while recording, writes `Source.LatestFrame` to
`{Directory}/sessions/{name}/000001.jpg …` every `IntervalSeconds` (new frames from an available camera only).
It never talks to rpicam itself, so the camera runs unchanged.

- Entities: `timelapse` switch (ON = recording; OFF stops and queues the render) and `timelapse_rendering`
  binary sensor. They are created in `TimelapseEntities`, apart from the service, because the service needs
  `IEntityRegistry`, which collects them. Starting can turn the camera on through its `{camera}_active` switch.
- State lives on disk (`session.json`: recording / queued / failed), so a restart resumes the recording and
  re-renders queued sessions. Limits: `MaxDurationHours`, `MinFreeMegabytes`.
- Rendering: one ffmpeg at a time, idle priority (`nice 19`), `-progress pipe:1` for the percentage; output to
  `videos/{name}.mp4` (+ `.jpg` thumbnail, `.json` metadata), then the frames are deleted. Failures keep the
  frames (retry/discard via the API); the default hardware encoder (`h264_v4l2m2m`) falls back to libx264.
- ffmpeg is required; `Ffmpeg` detects it (re-checks every 10 s while missing) and can install it with
  `sudo -n apt-get install ffmpeg` on request, then re-queues failed renders.
- With a linked printer (`IPrinterConnection`, optional): the trigger is `Layers` while it is connected and printing
  and `LayerFrames` is on (timer frames only with `TimerFramesBetweenLayers`; `LayerMinInterval` skips layers sooner
  than the interval), `Interval` otherwise. Layer events are queued and handled one at a time; a frame is saved only
  if newer than the last saved one (timer and layer can race). `FollowPrinter` starts/stops the recording.
  If the camera reports `CapturesOnPrinterLayer`, the timelapse waits (≤ 5 s) for the first frame after the layer
  change (`WaitForFrameAfterAsync`) instead of taking `LatestFrame`.
- Adapters see it only through `ITimelapseService` (Core): `TimelapseApi` maps `/api/timelapse/*` when it is
  registered. Names are validated (`[A-Za-z0-9][A-Za-z0-9_-]*`) before touching files.

## Adapters

### MQTT (`EnclosureHub.Mqtt`)

- One connection with Last Will `{base}/status = offline` (retained).
- On connect and whenever Home Assistant publishes `online` on `homeassistant/status`:
  publish discovery configs (retained), `online`, and all current states.
- `StateChanged` → queued → state published immediately (retained).
- Subscribed to `{base}/+/set`; `ON`/`OFF` on a switch's command topic calls `SetAsync`. The state is
  published only when the module reports the new state (no optimistic update), so HA shows reality.
  The camera module's `camera_active` switch persists `Modules:Camera:Active` and the service reacts via
  its options monitor – the same path the dashboard and the HTTP command use. Its state is the *real*
  state (frames arriving within `StaleAfterSeconds`, with the same grace period after a start), so one
  entity covers both "switched on" and "running"; ON sent to an already-on but stuck camera restarts rpicam-vid.
- Camera availability: the discovery config lists the device status *and* `{base}/{id}/available`
  (`availability_mode: all`). The camera module sets `CameraEntity.IsAvailable` together with the
  switch state; while false the bridge publishes `offline` and, in image mode, the embedded
  "CAMERA OFF" JPEG (`CameraPlaceholder.OffJpeg`, also served by the snapshot endpoint) so no stale
  frame survives on a dashboard.
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
- Camera runs one long-lived `rpicam-vid` at 1 fps, software MJPEG – about 15 % CPU; cost grows linearly with fps.
- Layer frames below 1 fps: rpicam-vid runs at `OnDemandFramerate` (1) and the service publishes only the regular
  and the layer frames ("selective mode"). rpicam-vid `--signal` pause was measured first and rejected: it keeps
  encoding while paused (70 % CPU at 5 fps).
- Timelapse capture is a file write every interval; rendering is CPU-heavy but runs at idle priority.
- Publish self-contained with ReadyToRun to cut startup time; never build on the Pi.
