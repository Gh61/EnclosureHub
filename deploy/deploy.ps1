# Builds for Raspberry Pi (32-bit ARM) and deploys over SSH.
# Usage:  ./deploy/deploy.ps1 -Target pi@192.168.1.50
# Requires SSH key login and passwordless sudo for systemctl on the Pi.

param(
    [Parameter(Mandatory = $true)]
    [string] $Target,

    [string] $RemoteDir = "/opt/enclosurehub"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root "publish"

dotnet publish (Join-Path $root "src/EnclosureHub.App") `
    -c Release -r linux-arm --self-contained `
    -p:PublishReadyToRun=true `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

ssh $Target "sudo systemctl stop enclosurehub 2>/dev/null; mkdir -p $RemoteDir"
scp -r "$publishDir/*" "${Target}:$RemoteDir/"
ssh $Target "chmod +x $RemoteDir/enclosurehub && sudo systemctl start enclosurehub && systemctl --no-pager status enclosurehub | head -5"
