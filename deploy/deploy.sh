#!/usr/bin/env bash
# Builds for Raspberry Pi (32-bit ARM) and deploys over SSH.
# Usage:  ./deploy/deploy.sh pi@192.168.1.50
# Requires SSH key login and passwordless sudo for systemctl on the Pi.
set -euo pipefail

TARGET="${1:?Usage: $0 user@host}"
REMOTE_DIR="${2:-/opt/enclosurehub}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"

dotnet publish "$ROOT/src/EnclosureHub.App" \
  -c Release -r linux-arm --self-contained \
  -p:PublishReadyToRun=true \
  -o "$ROOT/publish"

ssh "$TARGET" "sudo systemctl stop enclosurehub 2>/dev/null; mkdir -p $REMOTE_DIR"
scp -r "$ROOT/publish/." "$TARGET:$REMOTE_DIR/"
ssh "$TARGET" "chmod +x $REMOTE_DIR/enclosurehub && sudo systemctl start enclosurehub && systemctl --no-pager status enclosurehub | head -5"
