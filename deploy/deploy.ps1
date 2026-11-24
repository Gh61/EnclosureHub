# Builds for Raspberry Pi and deploys over SSH.
# Usage:  ./deploy/deploy.ps1 -Target pi@192.168.1.50 [-Install]
#   -Install   run `sudo enclosurehub --install` afterwards (first deployment)
#   -Runtime   linux-arm (default, 32-bit OS) or linux-arm64
# Requires SSH key login and passwordless sudo on the Pi.

param(
    [Parameter(Mandatory = $true)]
    [string] $Target,

    [string] $RemoteDir = "/opt/enclosurehub",

    [string] $Runtime = "linux-arm",

    [switch] $Install
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root "publish"

dotnet publish (Join-Path $root "src/EnclosureHub.App") `
    -c Release -r $Runtime --self-contained `
    -p:PublishReadyToRun=true `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# Stop the service if it is installed; make sure the directory exists and belongs to the SSH user.
# The dashboard build has hashed file names: clear wwwroot so no stale assets stay behind.
ssh $Target ('sudo systemctl stop enclosurehub 2>/dev/null; sudo mkdir -p ' + $RemoteDir + ' && sudo chown "$USER" ' + $RemoteDir + ' && rm -rf ' + $RemoteDir + '/wwwroot')
if ($LASTEXITCODE -ne 0) { throw "ssh failed" }

scp -r "$publishDir/*" "${Target}:$RemoteDir/"
if ($LASTEXITCODE -ne 0) { throw "scp failed" }

ssh $Target "chmod +x $RemoteDir/enclosurehub"

if ($Install) {
    ssh $Target "sudo $RemoteDir/enclosurehub --install"
}
else {
    ssh $Target ('if systemctl is-enabled enclosurehub >/dev/null 2>&1; then sudo systemctl start enclosurehub && systemctl --no-pager status enclosurehub | head -5; ' +
                 'else echo "Service not installed yet. Run: sudo ' + $RemoteDir + '/enclosurehub --install"; fi')
}
