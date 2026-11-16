# CLAUDE.md

Guidance for Claude Code working in this repository.

## Project

Enclosure Hub – modular .NET 10 controller for 3D printer enclosure accessories on a Raspberry Pi
(camera, sensors; lights, fans and a display are planned). Integrates with Home Assistant via MQTT
discovery and exposes a local HTTP API + SSE for displays. Read `docs/ARCHITECTURE.md` first,
`docs/ROADMAP.md` for planned work.

## Status

M1 (camera + system sensors + MQTT discovery + HTTP API/SSE + `--install`) builds, passes
`dotnet test` and runs on the target Pi as a systemd service. Next milestones: see `docs/ROADMAP.md`.

## Commands

```bash
dotnet build
dotnet test
dotnet run --project src/EnclosureHub.App
dotnet publish src/EnclosureHub.App -c Release -r linux-arm --self-contained -p:PublishReadyToRun=true -o publish
./deploy/deploy.sh <user>@<pi>      # or deploy/deploy.ps1 -Target <user>@<pi> on Windows
sudo /opt/enclosurehub/enclosurehub --install   # on the Pi, once: creates/enables/starts the systemd unit
```

Target device: Raspberry Pi 2 Model B (32-bit ARMv7, 1 GB RAM), Raspberry Pi OS Lite 13 (Trixie).
Access over SSH with key auth; `sudo` is passwordless. Service: `enclosurehub` (systemd),
logs: `journalctl -u enclosurehub`. Config on the device: `/opt/enclosurehub/appsettings.Local.json`.

## Conventions

- English for code, comments, docs and commit messages.
- Keep modules independent: hardware modules only create/update entities; adapters (MQTT, HTTP) never
  reference hardware modules. New hardware = new module + `Add…` extension + config flag in `Program.cs`.
- Entity IDs are lowercase snake_case and must stay stable (used in MQTT topics and HA unique IDs).
- Raise `StateChanged` only on real changes (use the entity's setters).
- Do not set `RuntimeIdentifier` in csproj files (keeps Windows debugging working).
- Mind the Pi 2: avoid allocations in hot loops, no polling faster than needed, no heavy dependencies.
- Stay on MQTTnet 4.x unless deliberately migrating everything to 5.x (API differs).
- Never commit secrets; device-specific settings go to `appsettings.Local.json` (gitignored, not published).
- When editing existing code, change only what the task requires – keep formatting and names.
