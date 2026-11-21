# Roadmap

## M1 – Camera + Home Assistant ✅

Camera module, system sensors, MQTT bridge with discovery, HTTP API with SSE, `--install` for the
systemd service. Verified on a Raspberry Pi 2 with Camera Module 2 (about 70 MB RSS, 1 fps at 1640×1232).

## Timelapse ✅

Module `EnclosureHub.Timelapse`: a frame every N seconds from the camera's latest image, MP4 rendered with
ffmpeg after stopping, library on the dashboard (play, download, delete), `timelapse` switch +
`timelapse_rendering` sensor in Home Assistant, ffmpeg installable from the dashboard.

Still open:

- **Verify the hardware encoder on the Pi 2** (`h264_v4l2m2m`; the software path falls back automatically) and
  measure libx264 render times for a 48 h recording; pick the default accordingly.
- **Retention** – keep the last N videos / delete videos older than X days (settings), so the SD card never
  fills up unattended.
- **Video management over MQTT** – proposal below, not implemented.

### Proposal: timelapse videos in Home Assistant (MQTT)

MQTT is a poor transport for files (an MP4 of a long print is tens of MB), so Home Assistant should get
*metadata, events and commands* over MQTT and fetch the files over HTTP when needed:

1. **"Video ready" event** – HA `event` entity via discovery (`event` component, `event_types: [video_ready,
   render_failed]`), state topic `{base}/timelapse/event` with
   `{"event_type":"video_ready","name":"timelapse_…","url":"http://hub:8080/api/timelapse/videos/….mp4",
   "thumbnail_url":"….jpg","duration":16,"size":24000000}`. Automations can then send a notification with the
   thumbnail and a link, or copy the file into HA's media folder with `downloader.download_file` (viewable in
   the HA media browser, backed up with HA).
2. **Library state** – a `sensor` `timelapse_videos` (count) whose JSON attributes (`{base}/timelapse_videos/attributes`)
   list the newest videos (name, created, duration, size, URLs) – enough for a Markdown card with links. Plus
   `sensor` `timelapse_storage_free` (MB) for alerts.
3. **Commands** – a `select` `timelapse_video` (options = video names, republished with discovery when the list
   changes – keep it to the last ~20) and `button`s acting on the selection: *Delete video*, *Render again*.
   A JSON command topic `{base}/timelapse/command` for scripts:
   `{"action":"delete","name":"…"}`, `{"action":"delete_older_than","days":30}`, `{"action":"retry","name":"…"}`.
   Destructive commands only by exact name (no wildcards) and logged.
4. Entity model changes needed: a text-capable sensor / JSON attributes for any entity (today only cameras
   have attributes), and new kinds `Button`, `Select`, `Event` mapped in `HomeAssistantDiscovery`; the timelapse
   module would create them, so the MQTT bridge stays module-agnostic.

## M2 – Lighting

Goal: LED strip inside the enclosure, controllable from Home Assistant and displays; bright neutral light for the camera.

Hardware (reference build):
- 24 V LED strip, 90 LEDs/m, separate RGB, cold white (6500 K) and warm white (2700 K) chips → 5 PWM channels.
- Logic-level MOSFET board (low side), driven by either
  - Raspberry Pi hardware PWM (2 channels: GPIO 12/13 or 18/19, `dtoverlay=pwm-2chan`) – enough for CW + WW, or
  - **PCA9685** I²C PWM board (16 channels) – all five channels plus fans. Binding exists in `Iot.Device.Bindings`.
- Single 24 V supply; DC-DC buck 24 → 5.1 V (≥ 3 A) for the Pi.

Software:
- New project `EnclosureHub.Gpio` with `PwmLight : LightEntity`.
  Config maps channels: `{ "Id": "enclosure_light", "Channels": { "Red": 0, "Green": 1, "Blue": 2, "Cold": 3, "Warm": 4 }, "Driver": "Pca9685" }`.
- Color temperature → mix of cold/warm channels; RGB → R/G/B channels; brightness scales all.
- Gamma correction (perceptual dimming).
- **Camera banding:** low PWM frequencies cause horizontal stripes on rolling-shutter cameras.
  Use ≥ 1 kHz and/or a "camera light" preset at 100 % duty.
- MQTT: Home Assistant **JSON schema light**
  - discovery: `schema: json`, `command_topic: {base}/{id}/set`, `state_topic: {base}/{id}/state`,
    `brightness: true`, `supported_color_modes: ["color_temp", "rgb"]`, `color_temp_kelvin: true`,
    `min_kelvin` / `max_kelvin`
  - state payload: `{"state":"ON","brightness":200,"color_mode":"color_temp","color_temp":4000}`
  - the bridge already subscribes to `{base}/+/set` and handles `ON`/`OFF` for switches; add a
    `LightEntity` case that parses the JSON payload → `LightCommand` → `SetAsync`.
- Optional alternative driver: WLED controller over HTTP/JSON (same `LightEntity`, different implementation).

## M3 – Fans and enclosure climate

- `PwmFan : FanEntity` (4-pin PC fan, 25 kHz PWM ideal; PCA9685 max ~1.5 kHz works but may be audible).
- Optional tachometer input (GPIO edge counting) → RPM `SensorEntity`.
- Temperature/humidity sensor (BME280 or SHT31 over I²C) → `SensorEntity` ×2.
- MQTT: Home Assistant **fan** – `command_topic` (`ON`/`OFF`), `percentage_command_topic`,
  `percentage_state_topic`, `state_topic`.
- Automations stay in Home Assistant (e.g. PrusaLink "printing" + temperature → fan speed).
  Optionally a local fallback rule in the hub (e.g. temperature > X → fan 100 %) so it works without HA.

## M4 – Display

The hub only provides the contract (`/api/entities`, `/api/events`, `POST …/command`, camera endpoints);
displays are clients. Possible clients, not mutually exclusive:

| Option | Pros | Cons |
|---|---|---|
| **Web UI** served by the hub (`wwwroot`), opened in a kiosk browser on any device | Zero install, works on tablets/phones too | Kiosk browser on a Pi 2 is heavy |
| **Avalonia** app on the Pi with the official 7" DSI touch display (DRM/framebuffer, no X) | Native, single device | Slow-ish on Pi 2, more work |
| **ESP32 touch display** (ESPHome / openHASP) via Home Assistant or the hub API | Cheap, instant on, low power | Separate device |

Suggested first step: a small static web UI (vanilla JS) in `EnclosureHub.App/wwwroot` using SSE –
it doubles as the reference client for the other options.

## M5 – Printer awareness (optional)

- Read PrusaLink locally (`/api/v1/status`) to show job progress on the display without Home Assistant.
- Expose as sensors (state, progress %, time remaining).

## Ideas

- Timelapse per layer (needs a layer-change signal from the printer, e.g. PrusaLink or a G-code macro) instead of per N seconds.
- Door sensor (reed switch) → `BinarySensorEntity`.
- Upload snapshots to Prusa Connect (optional, cloud).
