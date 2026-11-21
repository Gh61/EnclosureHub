# Setup

## 1. Raspberry Pi OS

1. Flash **Raspberry Pi OS Lite (32-bit)** with Raspberry Pi Imager (64-bit if your Pi supports it and you
   publish with `-r linux-arm64`).
2. In Imager's OS customisation set **hostname, user + password, Wi-Fi (country!), enable SSH**.
   There is no default user/password on current Raspberry Pi OS.
3. Boot (first boot takes a few minutes on older Pis), then `ssh <user>@<hostname>.local`.
4. Update: `sudo apt update && sudo apt full-upgrade -y`

### Networking tips

- Give the Pi a fixed address via a **DHCP reservation in your router** (simplest), or with `sudo nmtui`.
- Disable Wi-Fi power saving to avoid dropouts:
  ```bash
  sudo nmcli connection modify "<connection name>" 802-11-wireless.powersave 2
  ```
- Run `nmcli`/`nmtui` commands that change settings with `sudo`. Without it, PolicyKit waits for an
  authentication prompt that never appears over SSH and the command fails with *"Timeout was reached"*.
- Network names with spaces must be quoted: `"My Network"`.

### USB Wi-Fi adapters with Realtek RTL88x2BU (rtw88_8822bu)

Observed with a TP-Link Archer T3U Plus on a Pi 2 (kernel 6.18): authentication succeeds but
**association on 2.4 GHz always times out** when 802.11n is enabled; 5 GHz works fine.
NetworkManager then misleadingly keeps asking for the password.
How to diagnose:

```bash
sudo dmesg | grep wlan0        # "association with … timed out", no "RX AssocResp"
```

A test with `wpa_supplicant` and `disable_ht=1` connects on 2.4 GHz, so the problem is in 802.11n handling;
setting the router to 20 MHz did not help. Options: use 5 GHz (what this project uses), Ethernet, or an adapter
with a MediaTek/Ralink chipset (MT7601U, RT5370, MT7610U/MT7612U). Also recommended for rtw88:

```bash
echo "options rtw88_core disable_lps_deep=y" | sudo tee /etc/modprobe.d/rtw88.conf
```

## 2. Camera

Connect the ribbon cable to the CAMERA connector (on Pi 2/3 the blue side faces the Ethernet port).

```bash
rpicam-hello --list-cameras     # must list e.g. imx219
rpicam-still -n -o test.jpg
rpicam-vid -t 5000 -n --codec mjpeg --framerate 1 --width 1640 --height 1232 -o test.mjpeg
```

- `rpicam-*` missing → `sudo apt install rpicam-apps-lite`
- Camera not detected → in `/boot/firmware/config.txt` set `camera_auto_detect=0` and add `dtoverlay=imx219`
  (or the overlay for your sensor), then reboot.
- Image upside down → `"ExtraArgs": "--rotation 180"` in `Modules:Camera`.

Timelapse videos need **ffmpeg** (about 100 MB). Either install it now, or later with one click in the
dashboard (*Settings → Timelapse → Install*; works because the service user has passwordless sudo):

```bash
sudo apt install ffmpeg
```

Frames and videos are stored in `/opt/enclosurehub/timelapse` (deployments do not touch it). A 48-hour
recording at the default 30 s interval needs roughly 1.7 GB of free space before it is rendered.

## 3. MQTT broker in Home Assistant

1. *Settings → Add-ons → Add-on store* → **Mosquitto broker** → Install → Start (enable *Start on boot*).
   Running HA in Docker without add-ons? Use the `eclipse-mosquitto` container instead.
2. *Settings → People → Users* → add a user, e.g. `enclosurehub` (no admin rights needed).
   The Mosquitto add-on authenticates against Home Assistant users.
   The *Users* tab is only visible with *Advanced mode* enabled in your profile.
3. *Settings → Devices & services* → **MQTT** should be discovered → Configure.
4. Optional test from the Pi:
   ```bash
   sudo apt install mosquitto-clients
   mosquitto_pub -h <ha-address> -u enclosurehub -P <password> -t test -m hello
   ```
   In HA: MQTT → Configure → *Listen to a topic* `test`.

## 4. Deploy

Set up SSH key login from your PC (needed by the deploy scripts, and handy for Claude Code):

```powershell
# Windows PowerShell
ssh-keygen -t ed25519
type $env:USERPROFILE\.ssh\id_ed25519.pub | ssh <user>@<pi> "mkdir -p ~/.ssh && cat >> ~/.ssh/authorized_keys"
```
```bash
# Linux/macOS
ssh-copy-id <user>@<pi>
```

Check that sudo works without a password (default for the Imager-created user): `ssh <user>@<pi> sudo -n true && echo OK`

Build on your PC and copy to `/opt/enclosurehub` on the Pi (requires the .NET 10 SDK; nothing is built
or installed on the Pi – the app is a self-contained .NET executable):

```bash
./deploy/deploy.sh <user>@<pi>                 # Linux/macOS
./deploy/deploy.ps1 -Target <user>@<pi>        # Windows
```

Install the systemd service (once). It runs as the user who invokes `sudo`, is enabled at boot and
started immediately; the command also checks that `rpicam-vid` is installed:

```bash
ssh <user>@<pi>
sudo /opt/enclosurehub/enclosurehub --install
```

(`deploy.sh … --install` / `deploy.ps1 … -Install` does both steps in one go.)

Local configuration on the Pi – `/opt/enclosurehub/appsettings.Local.json`
(not touched by deployments; the web dashboard writes its settings into this file too):

```json
{
  "Mqtt": { "Host": "192.168.1.10", "Username": "enclosurehub", "Password": "…" },
  "Web": { "Auth": { "Username": "admin", "Password": "…" } }
}
```

`Web:Auth` is optional. When set, the browser asks for the name and password once; the API, the
camera endpoints and the dashboard all require it (only `/health` stays open).

```bash
chmod 600 /opt/enclosurehub/appsettings.Local.json
sudo systemctl restart enclosurehub
journalctl -u enclosurehub -f
```

Later deployments just run the deploy script again (it stops the service, copies the new build and starts it).
To remove the service: `sudo /opt/enclosurehub/enclosurehub --uninstall` (keeps `/opt/enclosurehub` and its configuration).

## 5. Home Assistant

- *Settings → Devices & services → MQTT → Devices → Printer Enclosure*: Camera, Camera active (switch;
  ON = switched on and producing frames), Timelapse (switch; ON = recording, OFF = stop and render),
  Timelapse rendering, CPU temperature, Wi-Fi signal.
- Timelapse of every print: see the automation in the README (*Timelapse* section).
- Dashboard: *Picture Entity* / *Picture Glance* card with the camera entity.
- Camera only while printing – an automation on the PrusaLink printer state (adjust the entity ids to yours):

  ```yaml
  alias: Enclosure camera follows the printer
  trigger:
    - platform: state
      entity_id: sensor.prusa_mk4s_printer_state
  action:
    - service: >
        switch.turn_{{ 'on' if trigger.to_state.state in ['printing', 'paused', 'attention'] else 'off' }}
      target:
        entity_id: switch.printer_enclosure_camera_active
  mode: queued
  ```

  The switch state in HA follows the real camera state (the hub reports back once the process has
  stopped or started), so the automation is safe to re-run. While the camera is off, the camera entity
  is *unavailable* and shows a "CAMERA OFF" picture rather than the last frame of the previous print.
- Fast camera modes (1080p30, 720p60, 480p90 in the hub's *Settings → Camera*): switch *Settings → Home Assistant →
  Camera in Home Assistant* to **Link**. The camera then appears as an MQTT *image* entity that Home Assistant
  fetches from the hub every few seconds, and its attributes contain `stream_url` for the MJPEG integration below.
  If `Web:Auth` is enabled, put the credentials into the hub address (`http://user:password@<pi>:8080`) so HA can fetch.
- Optional live view: **MJPEG IP Camera** integration with
  MJPEG URL `http://<pi>:8080/api/cameras/camera/stream.mjpeg` and
  still image URL `http://<pi>:8080/api/cameras/camera/snapshot.jpg`
  (enter the `Web:Auth` name and password there if you enabled authentication).
- Printer status: the built-in **PrusaLink** integration – combine it with these entities in automations.

## Troubleshooting

| Symptom | Check |
|---|---|
| No image | `journalctl -u enclosurehub` – lines starting with `rpicam:`; run `rpicam-hello --list-cameras` |
| `MQTT error: … (NotAuthorized)` in the log | The broker rejects the username/password. Check the user in HA (*Settings → People → Users*) and the values in `appsettings.Local.json`; test with `mosquitto_sub -h <ha> -u … -P … -t test -C 1` |
| Device missing in HA | `mosquitto_sub -h <ha> -u … -P … -t 'homeassistant/#' -v` |
| Stale entity from an older version (e.g. *Camera running*) | Clear its retained discovery config: `mosquitto_pub -h <ha> -u … -P … -r -n -t homeassistant/binary_sensor/enclosurehub/camera_running/config` (and `-t enclosurehub/camera_running/state`) |
| Entities "unavailable" | `enclosurehub/status` must be `online` |
| Dashboard answers 404 | `Web:Dashboard:Enabled` is false in `appsettings.Local.json` – set it to true and restart |
| Forgot the dashboard password | Edit or remove `Web:Auth` in `appsettings.Local.json` and restart |
| Camera settings don't apply | `journalctl -u enclosurehub` should show `Camera settings changed` followed by `Starting rpicam-vid …`; the service must be able to write `appsettings.Local.json` |
| Timelapse video not rendered | Dashboard *Timelapse* page shows the error; `journalctl -u enclosurehub | grep -i timelapse`. Missing ffmpeg → *Settings → Timelapse → Install*, then the recording is rendered automatically |
| ffmpeg *Install* fails with "password" | The service user needs passwordless sudo; run `sudo apt install ffmpeg` on the Pi instead |
| Service won't start | `systemctl status enclosurehub`; re-run `sudo /opt/enclosurehub/enclosurehub --install` after changing users |
