param([Parameter(Mandatory=$true)][string]$Pdf)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$bin = Join-Path $root 'CountFile2\bin\Debug'
$exe = Join-Path $bin 'PaddleSmoke.exe'
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe') /nologo /target:exe /platform:x86 "/out:$exe" "/r:$bin\CountFile2.exe" (Join-Path $PSScriptRoot 'PaddleSmoke.cs')
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed. Build CountFile2 Debug first.' }
Copy-Item (Join-Path $bin 'CountFile2.exe.config') "$exe.config" -Force
$destination = Join-Path $root ('tmp\paddle-smoke-' + [Guid]::NewGuid().ToString('N'))
& $exe (Join-Path $root '.paddleocr-venv\Scripts\python.exe') (Join-Path $bin 'PaddleOcr\worker.py') $Pdf $destination
if ($LASTEXITCODE -ne 0) { throw 'Real PaddleOCR smoke failed.' }
