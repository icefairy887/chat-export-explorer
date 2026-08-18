[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$modelDirectory = Join-Path $PSScriptRoot 'Models\all-MiniLM-L6-v2'
$modelPath = Join-Path $modelDirectory 'model.onnx'
$vocabPath = Join-Path $modelDirectory 'vocab.txt'

$modelUrl = 'https://huggingface.co/nsense/all-MiniLM-L6-v2-onnx/resolve/main/model.onnx'
$vocabUrl = 'https://huggingface.co/nsense/all-MiniLM-L6-v2-onnx/resolve/main/vocab.txt'
$modelSha256 = '207B1F1295D3EE84AD8E0EEA2C6F4E54FEB0EC5586E13B722703970C9239FF2A'
$vocabSha256 = '07ECED375CEC144D27C900241F3E339478DEC958F92FDDBC551F295C992038A3'

New-Item -ItemType Directory -Path $modelDirectory -Force | Out-Null

function Install-VerifiedFile {
    param(
        [Parameter(Mandatory)] [string] $Url,
        [Parameter(Mandatory)] [string] $Destination,
        [Parameter(Mandatory)] [string] $ExpectedSha256
    )

    if (Test-Path -LiteralPath $Destination) {
        $existingHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $Destination).Hash
        if ($existingHash -eq $ExpectedSha256) {
            Write-Host "Already verified: $(Split-Path -Leaf $Destination)"
            return
        }
    }

    $temporaryPath = "$Destination.download"
    Write-Host "Downloading $(Split-Path -Leaf $Destination)..."
    Invoke-WebRequest -Uri $Url -OutFile $temporaryPath
    $downloadHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $temporaryPath).Hash

    if ($downloadHash -ne $ExpectedSha256) {
        Remove-Item -LiteralPath $temporaryPath -Force
        throw "Downloaded file failed SHA-256 verification: $Destination"
    }

    Move-Item -LiteralPath $temporaryPath -Destination $Destination -Force
    Write-Host "Installed and verified: $(Split-Path -Leaf $Destination)"
}

Install-VerifiedFile -Url $modelUrl -Destination $modelPath -ExpectedSha256 $modelSha256
Install-VerifiedFile -Url $vocabUrl -Destination $vocabPath -ExpectedSha256 $vocabSha256

Write-Host 'Lumina local model is ready.'
