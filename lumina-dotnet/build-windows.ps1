[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $PSScriptRoot 'artifacts'
$suiteDirectory = Join-Path $artifactRoot 'Lumina-Suite-win-x64'
$zipPath = Join-Path $artifactRoot 'Lumina-Suite-win-x64.zip'
$archiveBuildVenv = Join-Path $artifactRoot 'archive-build-venv'
$archivePython = Join-Path $archiveBuildVenv 'Scripts\python.exe'
$archiveExplorerSource = Join-Path $repositoryRoot 'dist\Chat Export Explorer Server'

& (Join-Path $PSScriptRoot 'setup.ps1')

if (-not (Test-Path -LiteralPath $archivePython)) {
    python -m venv $archiveBuildVenv
}

& $archivePython -m pip install --disable-pip-version-check `
    -r (Join-Path $repositoryRoot 'requirements-archive-server.txt')
& $archivePython -m PyInstaller --clean --noconfirm `
    (Join-Path $repositoryRoot 'Chat Export Explorer.spec') `
    --distpath (Join-Path $repositoryRoot 'dist') `
    --workpath (Join-Path $repositoryRoot 'build')

if (Test-Path -LiteralPath $suiteDirectory) {
    Remove-Item -LiteralPath $suiteDirectory -Recurse -Force
}

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

dotnet publish (Join-Path $PSScriptRoot 'ChatAnalyzer.Desktop\ChatAnalyzer.Desktop.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $suiteDirectory `
    --nologo

if (Test-Path -LiteralPath $archiveExplorerSource) {
    $archiveTarget = Join-Path $suiteDirectory 'Archive Explorer'
    New-Item -ItemType Directory -Path $archiveTarget -Force | Out-Null
    Copy-Item -Path (Join-Path $archiveExplorerSource '*') -Destination $archiveTarget -Recurse -Force
}

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $suiteDirectory -Force
Compress-Archive -Path (Join-Path $suiteDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "Lumina Suite created: $zipPath"
