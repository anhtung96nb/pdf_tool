param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug', [ValidateSet('x86', 'x64')][string]$Architecture = 'x86')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild/**/Bin/MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'MSBuild not found.' }
& $msbuild (Join-Path $root 'CountFile2.sln') /t:Build "/p:Configuration=$Configuration" /verbosity:quiet /nologo /nr:false
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$bin = Join-Path $root "CountFile2/bin/$Configuration"
$exe = Join-Path $bin "PdfCompressionTests-$Architecture.exe"
& (Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe') /nologo /target:exe "/platform:$Architecture" "/out:$exe" "/r:$bin/CountFile2.exe" "/r:$bin/itextsharp.dll" "/r:$bin/PdfiumViewer.dll" /r:System.Drawing.dll /r:System.Windows.Forms.dll (Join-Path $PSScriptRoot 'PdfCompressionTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
Copy-Item -LiteralPath (Join-Path $bin 'CountFile2.exe.config') -Destination "$exe.config" -Force
& $exe (Join-Path $root ('tmp/pdf-compression-tests/run-' + $Configuration + '-' + $Architecture + '-' + [Guid]::NewGuid().ToString('N')))
if ($LASTEXITCODE -ne 0) { throw 'PDF compression tests failed.' }
