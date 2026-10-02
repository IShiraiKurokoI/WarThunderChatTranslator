$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$modelName = "sherpa-onnx-paraformer-zh-small-2024-03-09"
$modelDir = Join-Path $repoRoot "WarThunderChatTranslator\Assets\SpeechModels\$modelName"
$modelPath = Join-Path $modelDir "model.int8.onnx"
$tokensPath = Join-Path $modelDir "tokens.txt"
$archiveUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/$modelName.tar.bz2"
$directModelUrl = "https://huggingface.co/csukuangfj/$modelName/resolve/main/model.int8.onnx?download=true"
$directTokensUrl = "https://huggingface.co/csukuangfj/$modelName/raw/main/tokens.txt"
$expectedModelHash = "3EF6C19369B912F7CAF3CEF8E545C5CCD1A33D9D7EC792A46668DC41C4B229EC"

New-Item -ItemType Directory -Force -Path $modelDir | Out-Null

function Test-ModelFiles {
    if (-not (Test-Path $modelPath) -or -not (Test-Path $tokensPath)) { return $false }
    if ((Get-Item $tokensPath).Length -lt 50000) { return $false }
    $actualHash = (Get-FileHash -Algorithm SHA256 $modelPath).Hash.ToUpperInvariant()
    return $actualHash -eq $expectedModelHash
}

if (-not (Test-ModelFiles)) {
    Remove-Item $modelPath, $tokensPath -Force -ErrorAction SilentlyContinue
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("WTChatTranslator-ASR-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
    try {
        $tar = Get-Command tar.exe -ErrorAction SilentlyContinue
        if ($tar) {
            $archivePath = Join-Path $tempRoot "$modelName.tar.bz2"
            Write-Host "Downloading Paraformer INT8 model archive (~78 MB)..."
            Invoke-WebRequest -Uri $archiveUrl -OutFile $archivePath -UseBasicParsing
            & $tar.Source -xjf $archivePath -C $tempRoot
            if ($LASTEXITCODE -ne 0) { throw "tar.exe failed with exit code $LASTEXITCODE" }

            $extracted = Join-Path $tempRoot $modelName
            Copy-Item (Join-Path $extracted "model.int8.onnx") $modelPath -Force
            Copy-Item (Join-Path $extracted "tokens.txt") $tokensPath -Force
        }
        else {
            Write-Host "tar.exe was not found; downloading model files directly..."
            Invoke-WebRequest -Uri $directModelUrl -OutFile $modelPath -UseBasicParsing
            Invoke-WebRequest -Uri $directTokensUrl -OutFile $tokensPath -UseBasicParsing
        }
    }
    finally {
        Remove-Item $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if (-not (Test-Path $modelPath)) { throw "model.int8.onnx is missing after download." }
if (-not (Test-Path $tokensPath)) { throw "tokens.txt is missing after download." }

$actualHash = (Get-FileHash -Algorithm SHA256 $modelPath).Hash.ToUpperInvariant()
if ($actualHash -ne $expectedModelHash) {
    Remove-Item $modelPath -Force -ErrorAction SilentlyContinue
    throw "Model SHA-256 verification failed. Expected $expectedModelHash, got $actualHash. The downloaded model was removed."
}

if ((Get-Item $tokensPath).Length -lt 50000) {
    Remove-Item $tokensPath -Force -ErrorAction SilentlyContinue
    throw "tokens.txt is unexpectedly small; it was removed. Run this script again."
}

Write-Host "Speech model is ready: $modelDir"
