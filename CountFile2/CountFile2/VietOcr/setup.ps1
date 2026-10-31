param([string]$Python = 'python', [string]$EnvironmentPath = '')
$ErrorActionPreference = 'Stop'
if (-not $EnvironmentPath) { $EnvironmentPath = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) '.vietocr-venv' }
if (-not (Test-Path -LiteralPath (Join-Path $EnvironmentPath 'Scripts\python.exe'))) {
    & $Python -m venv $EnvironmentPath
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create Python environment. Use Python 3.11 x64.' }
}
$runtime = Join-Path $EnvironmentPath 'Scripts\python.exe'
& $runtime -m pip install -r (Join-Path $PSScriptRoot 'requirements.txt')
if ($LASTEXITCODE -ne 0) { throw 'Dependency installation failed.' }
& $runtime (Join-Path $PSScriptRoot 'prepare_models.py')
if ($LASTEXITCODE -ne 0) { throw 'Model preparation failed.' }
& $runtime -m pip freeze | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'models\runtime-versions.txt') -Encoding UTF8
Write-Host "Ready. Python: $runtime. Build the application again to copy the worker and model."
