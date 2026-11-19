[CmdletBinding()]
param(
    [string[]] $Models = @('lexical', 'all-MiniLM-L6-v2', 'BAAI/bge-m3', 'FacebookAI/roberta-base'),
    [int] $BatchSize = 8
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    $python = Join-Path $projectRoot '.venv\Scripts\python.exe'
    if (-not (Test-Path -LiteralPath $python)) {
        & py -3.12 -m venv .venv
        if ($LASTEXITCODE -ne 0) { throw 'Install Python 3.12 with the Windows py launcher, then retry.' }
    }
    & $python -m pip install -r requirements-semantic.txt
    if ($LASTEXITCODE -ne 0) { throw 'Dependency installation failed.' }
    & $python -m unittest discover -s tests -v
    if ($LASTEXITCODE -ne 0) { throw 'Regression checks failed.' }
    & $python evaluate_embeddings.py --models @Models --batch-size $BatchSize
    if ($LASTEXITCODE -ne 0) { throw 'One or more models failed. Check artifacts\embedding-evaluation.json.' }
    Write-Host 'Comparison complete: artifacts\embedding-evaluation.json'
}
finally {
    Pop-Location
}
