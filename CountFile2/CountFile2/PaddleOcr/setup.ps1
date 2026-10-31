param([string]$Python = 'py')
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$venv = Join-Path $root '.paddleocr-venv'
if ($Python -eq 'py') {
    $localPython = Join-Path $env:LOCALAPPDATA 'Programs\Python\Python311\python.exe'
    if (Test-Path -LiteralPath $localPython) { & $localPython -m venv $venv }
    else { & py -3.11 -m venv $venv }
} else { & $Python -m venv $venv }
if ($LASTEXITCODE -ne 0) { throw 'Cannot create Python 3.11 environment.' }
$exe = Join-Path $venv 'Scripts\python.exe'
& $exe -m pip install -r (Join-Path $PSScriptRoot 'requirements.txt')
if ($LASTEXITCODE -ne 0) { throw 'PaddleOCR installation failed.' }
& $exe (Join-Path $PSScriptRoot 'worker.py') --prepare
if ($LASTEXITCODE -ne 0) { throw 'Model download/initialization failed.' }
Write-Host "Ready. Select Python: $exe"
