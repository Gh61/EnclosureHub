# CLAUDE.md

Guidance for Claude Code working in this repository.

## Project

Enclosure Hub – modular .NET 10 controller for 3D printer enclosure accessories on a Raspberry Pi
(camera, timelapse, Prusa printer connection over USB, sensors; lights, fans and a display are planned). Integrates with Home Assistant via MQTT
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
cd src/EnclosureHub.App/Dashboard && npm run dev    # dashboard with hot reload, proxies /api to VITE_PROXY (default localhost:8080)
dotnet publish src/EnclosureHub.App -c Release -r linux-arm --self-contained -p:PublishReadyToRun=true -o publish
./deploy/deploy.sh <user>@<pi>      # or deploy/deploy.ps1 -Target <user>@<pi> on Windows
sudo /opt/enclosurehub/enclosurehub --install   # on the Pi, once: creates/enables/starts the systemd unit
```

Target device: Raspberry Pi 2 Model B (32-bit ARMv7, 1 GB RAM), Raspberry Pi OS Lite 13 (Trixie).
Access over SSH with key auth; `sudo` is passwordless. Service: `enclosurehub` (systemd),
logs: `journalctl -u enclosurehub`. Config on the device: `/opt/enclosurehub/appsettings.Local.json`.

## Conventions

- English for code, comments, docs and commit messages.
- Keep modules independent: hardware modules only create/update entities; adapters (MQTT, HTTP, web
  dashboard) never reference hardware modules. New hardware = new module + `Add…` extension (honouring
  `Modules:{Name}:Enabled`) + optional `ISettingsSection` for runtime-editable settings.
- The web dashboard is a Vue 3 + TypeScript + SCSS app in `src/EnclosureHub.App/Dashboard/` (Vite builds it into
  `wwwroot/`, which is generated and not committed; `dotnet build`/`publish` run `npm run build`, so Node.js is
  needed on the build PC, never on the Pi). It talks to `/api/*` only, in the black/white outlined style. Keep the
  type checks green (`npm run build` runs vue-tsc over scripts and templates); state lives in `src/stores/*.ts`,
  pages in `src/pages/`. Secrets (MQTT, `Web:Auth`) stay file-only, never editable in the UI.
- Entity IDs are lowercase snake_case and must stay stable (used in MQTT topics and HA unique IDs).
- Raise `StateChanged` only on real changes (use the entity's setters).
- Do not set `RuntimeIdentifier` in csproj files (keeps Windows debugging working).
- Mind the Pi 2: avoid allocations in hot loops, no polling faster than needed, no heavy dependencies.
- Stay on MQTTnet 4.x unless deliberately migrating everything to 5.x (API differs).
- Never commit secrets; device-specific settings go to `appsettings.Local.json` (gitignored, not published).
- Every new module or user-visible feature is documented in `README.md` (what it does, dashboard, settings/config
  keys, API endpoints, MQTT entities) in the same change – plus `docs/ARCHITECTURE.md` for design and
  `docs/SETUP.md` for installation steps where relevant.
- Dashboard screenshots in `docs/screenshots/` (JPEG, 1266 px wide, paired rows in the README padded to equal height)
  are refreshed when a page changes visibly or a new page is added.
- A new feature of one module must not change the behaviour of others by default (e.g. linking the printer does
  not start recording timelapses); new couplings are opt-in settings.
- When editing existing code, change only what the task requires – keep formatting and names.
