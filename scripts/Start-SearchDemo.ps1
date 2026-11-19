[CmdletBinding()]
param([string] $Model = 'BAAI/bge-m3', [int] $Port = 5000, [int] $BatchSize = 8)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$previousDatabase = $env:CHAT_DB
$previousModel = $env:CHAT_EMBEDDING_MODEL
Push-Location $projectRoot
try {
    $python = Join-Path $projectRoot '.venv\Scripts\python.exe'
    if (-not (Test-Path -LiteralPath $python)) {
        throw 'Run .\scripts\Test-Search.ps1 first to create the Python environment.'
    }
    if (-not (Test-Path -LiteralPath 'artifacts\search-demo.db')) {
        & $python create_search_demo.py
        if ($LASTEXITCODE -ne 0) { throw 'Demo creation failed.' }
    }
    $env:CHAT_DB = Join-Path $projectRoot 'artifacts\search-demo.db'
    $env:CHAT_EMBEDDING_MODEL = $Model
    & $python semantic_search.py --model $Model --build-only --batch-size $BatchSize
    if ($LASTEXITCODE -ne 0) { throw 'Semantic index build failed.' }
    & $python build_trajectories.py
    if ($LASTEXITCODE -ne 0) { throw 'Trajectory build failed.' }
    Write-Host "Open http://127.0.0.1:$Port/search in your browser. Select Meaning."
    Write-Host 'Press Ctrl+C here to stop the demo.'
    & $python archive_server.py --port $Port
    if ($LASTEXITCODE -ne 0) { throw 'The archive server stopped with an error.' }
}
finally {
    $env:CHAT_DB = $previousDatabase
    $env:CHAT_EMBEDDING_MODEL = $previousModel
    Pop-Location
}
