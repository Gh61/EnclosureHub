# Enclosure Hub

A small, modular controller for 3D printer enclosure accessories, running on a Raspberry Pi
and integrating with [Home Assistant](https://www.home-assistant.io/) – **fully local, no cloud**.

Built for a Prusa MK4S in an enclosure, but nothing in it is printer-specific.

| Status | Feature |
|---|---|
| ✅ | Camera (Raspberry Pi Camera Module via `rpicam-vid`) – snapshots, MJPEG stream |
| ✅ | Home Assistant integration via MQTT discovery (entities appear automatically) |
| ✅ | Local HTTP API + live event stream for displays and other clients |
| ✅ | Web dashboard (camera view, entity status, camera settings, restart) with optional Basic auth |
| ✅ | Controller health sensors (CPU temperature, camera running) |
| 🛠️ | LED lighting (PWM, RGB + tunable white) |
| 🛠️ | Enclosure fans (PWM) + temperature/humidity sensor |
| 🛠️ | Touch display UI |

See [docs/ROADMAP.md](docs/ROADMAP.md) for what is planned and how it will work.

## How it works

```
             ┌──────────── Enclosure Hub (Raspberry Pi) ────────────┐
 Hardware    │  Modules              Entities           Adapters    │   Clients
             │                                                      │
 Camera  ───►│  Camera (rpicam) ──► camera             ┌► MQTT ─────┼─► Home Assistant
             │                      camera_running ────┤             │
 /sys    ───►│  System sensors ──► cpu_temperature ────┤             │
             │                                         └► HTTP API ──┼─► Touch display,
 PWM/I²C ───►│  Lights, fans (planned) ► light, fan     (REST + SSE) │   browser, scripts
             └──────────────────────────────────────────────────────┘
```

- **Modules** talk to hardware and own **entities** (camera, sensor, binary sensor, light, fan).
- **Adapters** expose all entities to the outside world without knowing about hardware.
  Adding a fan module automatically makes the fan available in Home Assistant *and* on any display.

Details: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Hardware

Developed and tested on:

- Raspberry Pi 2 Model B v1.1 (1 GB RAM, 32-bit ARMv7) – anything newer works too
- Raspberry Pi Camera Module 2 (IMX219)
- Raspberry Pi OS Lite 13 (Trixie), 32-bit

## Quick start

1. Prepare the Pi and Home Assistant: [docs/SETUP.md](docs/SETUP.md)
2. Build and deploy from your PC (requires [.NET 10 SDK](https://dotnet.microsoft.com/download);
   nothing needs to be installed on the Pi except `rpicam-apps-lite`):

   ```bash
   ./deploy/deploy.sh pi@192.168.1.50           # Linux/macOS
   ./deploy/deploy.ps1 -Target pi@192.168.1.50  # Windows
   ```

3. On the Pi, install the systemd service (once):

   ```bash
   sudo /opt/enclosurehub/enclosurehub --install
   ```

4. On the Pi, create `/opt/enclosurehub/appsettings.Local.json` with your MQTT broker
   (see `src/EnclosureHub.App/appsettings.Local.example.json`) and restart:
   `sudo systemctl restart enclosurehub`
5. Open `http://<pi-address>:8080` – the web dashboard shows the camera.
   In Home Assistant the device **Printer Enclosure** appears under *Settings → Devices → MQTT*.

## Web dashboard

A small single-page UI served by the hub itself (black/white, works on phones too):

- **Camera** – live MJPEG view, running indicator, snapshot link.
- **Status** – all entities with live values (Server-Sent Events).
- **Settings** – camera on/off, resolution, frame rate, JPEG quality, rotation, extra rpicam arguments;
  the dashboard itself can be switched off here. Changes are written to `appsettings.Local.json`
  and applied live (the camera process restarts within a few seconds).
- **System** – version, host, uptime, memory and a *Restart service* button.

Settings → `Web:Dashboard:Enabled` turns the UI off (the API keeps working; re-enable it in the config file).
Set `Web:Auth:Username` / `Web:Auth:Password` to protect **everything** the server serves (dashboard, API,
camera endpoints; only `/health` stays open) with HTTP Basic authentication. It is plain HTTP on your LAN,
so treat it as a lock against accidental changes rather than strong security.

## Configuration

`appsettings.json` holds defaults; put device-specific values and secrets in
`appsettings.Local.json` next to the executable (not deployed, not committed).

| Section | Purpose |
|---|---|
| `Device` | Id, name, manufacturer, model shown in Home Assistant |
| `Web` | `Dashboard:Enabled`; `Auth:Username`/`Auth:Password` (Basic auth for all endpoints except `/health`) |
| `Modules:Camera` | `Enabled`, `Width`/`Height`, `Framerate`, `Quality`, `Rotation` (0/180), `ExtraArgs` (e.g. `--hflip --vflip`) |
| `Modules:SystemSensors` | `Enabled`, polling interval |
| `Mqtt` | `Enabled`, broker `Host`/`Port`/`Username`/`Password`, image interval |

The dashboard edits the camera and dashboard values of `appsettings.Local.json`; everything else is file-only.

Any value can also be set via environment variables, e.g. `Mqtt__Password=...`.

## HTTP API

| Endpoint | Description |
|---|---|
| `GET /` | Web dashboard (static files; 404 when `Web:Dashboard:Enabled` is false) |
| `GET /health` | Liveness check (never requires authentication) |
| `GET /api/entities` | All entities with current state |
| `GET /api/entities/{id}` | One entity |
| `POST /api/entities/{id}/command` | Control a light or fan, e.g. `{"on":true,"brightness":200}` |
| `GET /api/events` | Server-Sent Events – `state` event per entity on connect and on every change |
| `GET /api/cameras/{id}/snapshot.jpg` | Latest camera frame |
| `GET /api/cameras/{id}/stream.mjpeg` | MJPEG stream |
| `GET /api/settings` | Settings sections (fields + current values) exposed by the modules |
| `PUT /api/settings/{section}` | Apply settings, e.g. `{"enabled":false}` or `{"resolution":"1920x1080","quality":70}` |
| `GET /api/system` | Version, host, uptime, memory |
| `POST /api/system/restart` | Graceful shutdown; systemd restarts the service |

## MQTT topics

With the default `Device:Id` = `enclosurehub`:

| Topic | Content |
|---|---|
| `enclosurehub/status` | `online` / `offline` (retained, last will) |
| `enclosurehub/{entity}/state` | Entity state (retained) |
| `enclosurehub/{entity}/image` | Camera JPEG (every `CameraImageIntervalSeconds`, new frames only) |
| `homeassistant/{component}/enclosurehub/{entity}/config` | Home Assistant discovery (retained) |

## Development

```bash
dotnet build
dotnet test
dotnet run --project src/EnclosureHub.App   # runs on Windows/Linux too; camera just keeps retrying
```

## License

[MIT](LICENSE) © Tomáš Zenkner
