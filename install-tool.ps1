#!/usr/bin/env pwsh
# Build, pack and install aspire-manager as a dotnet global tool, into whichever directory the SDK uses
# for this OS (~/.dotnet/tools on macOS and Linux, %USERPROFILE%\.dotnet\tools on Windows).
#
#   ./install-tool.ps1              install the current working tree
#   ./install-tool.ps1 -Native      build a native binary for this machine's RID: faster startup, no
#                                   runtime needed, but it can only be built on the platform it targets
#   ./install-tool.ps1 -Uninstall   remove it again

[CmdletBinding()]
param(
    [switch] $Native,
    [switch] $Uninstall
)

$ErrorActionPreference = 'Stop'

$packageId = 'aspire-manager'
$project = Join-Path $PSScriptRoot 'src/AspireManager.Tui'

if ($Uninstall) {
    dotnet tool uninstall --global $packageId
    exit $LASTEXITCODE
}

# A unique version every run: NuGet caches by id and version, so reusing one risks installing the previous
# build from ~/.nuget/packages rather than what was just compiled.
$version = '0.0.0-local.' + (Get-Date -Format 'yyyyMMddHHmmss')
$output = Join-Path ([System.IO.Path]::GetTempPath()) "aspire-manager-pack-$version"

$packArgs = @('pack', $project, '-c', 'Release', '-o', $output, "-p:Version=$version")

if ($Native) {
    # Native AOT cannot cross-compile, so this only ever targets the machine it runs on.
    $rid = dotnet --info | Select-String -Pattern '^\s*RID:\s*(\S+)' | ForEach-Object { $_.Matches[0].Groups[1].Value }
    if (-not $rid) { throw 'Could not determine this machine''s RID from `dotnet --info`.' }
    Write-Host "Packing a native tool for $rid" -ForegroundColor Cyan
    $packArgs += @('-r', $rid)
}

& dotnet @packArgs
if ($LASTEXITCODE -ne 0) { throw 'pack failed' }

# Ignore the failure when it was not installed to begin with.
dotnet tool uninstall --global $packageId 2>$null | Out-Null

dotnet tool install --global --add-source $output $packageId --version $version
if ($LASTEXITCODE -ne 0) { throw 'install failed' }

Remove-Item -Recurse -Force $output -ErrorAction SilentlyContinue

Write-Host ''
Write-Host "Installed $packageId $version. Run: aspire-manager" -ForegroundColor Green
