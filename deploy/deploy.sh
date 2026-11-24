#!/usr/bin/env bash
# Builds for Raspberry Pi and deploys over SSH.
# Usage:  ./deploy/deploy.sh pi@192.168.1.50 [--install] [--runtime linux-arm64] [--dir /opt/enclosurehub]
#   --install   run `sudo enclosurehub --install` afterwards (first deployment)
#   --runtime   linux-arm (default, 32-bit OS) or linux-arm64
# Requires SSH key login and passwordless sudo on the Pi.
set -euo pipefail

TARGET="${1:?Usage: $0 user@host [--install] [--runtime RID] [--dir DIR]}"
shift
REMOTE_DIR="/opt/enclosurehub"
RUNTIME="linux-arm"
INSTALL=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --install) INSTALL=1 ;;
    --runtime) RUNTIME="$2"; shift ;;
    --dir)     REMOTE_DIR="$2"; shift ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
  shift
done

ROOT="$(cd "$(dirname "$0")/.." && pwd)"

dotnet publish "$ROOT/src/EnclosureHub.App" \
  -c Release -r "$RUNTIME" --self-contained \
  -p:PublishReadyToRun=true \
  -o "$ROOT/publish"

# Stop the service if it is installed; make sure the directory exists and belongs to the SSH user.
# The dashboard build has hashed file names: clear wwwroot so no stale assets stay behind.
ssh "$TARGET" "sudo systemctl stop enclosurehub 2>/dev/null; sudo mkdir -p $REMOTE_DIR && sudo chown \"\$USER\" $REMOTE_DIR && rm -rf $REMOTE_DIR/wwwroot"
scp -r "$ROOT/publish/." "$TARGET:$REMOTE_DIR/"
ssh "$TARGET" "chmod +x $REMOTE_DIR/enclosurehub"

if [[ $INSTALL -eq 1 ]]; then
  ssh "$TARGET" "sudo $REMOTE_DIR/enclosurehub --install"
else
  ssh "$TARGET" "if systemctl is-enabled enclosurehub >/dev/null 2>&1; then sudo systemctl start enclosurehub && systemctl --no-pager status enclosurehub | head -5; else echo 'Service not installed yet. Run: sudo $REMOTE_DIR/enclosurehub --install'; fi"
fi
