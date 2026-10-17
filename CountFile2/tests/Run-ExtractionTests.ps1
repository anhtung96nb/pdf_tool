param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug', [ValidateSet('x86', 'x64')][string]$Architecture = 'x86')
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$buildTool = & $vswherePath -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
$binaryDirectory = Join-Path $repositoryRoot "tmp\extraction-build\$Configuration"
& $buildTool (Join-Path $repositoryRoot 'CountFile2.sln') /t:Build "/p:Configuration=$Configuration" "/p:OutDir=$binaryDirectory\" /verbosity:quiet /nologo /nr:false
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$testExecutable = Join-Path $binaryDirectory "AdministrativeExtractionTests-$Architecture.exe"
& $compilerPath /nologo /target:exe "/platform:$Architecture" "/out:$testExecutable" "/r:$binaryDirectory\CountFile2.exe" "/r:$binaryDirectory\PdfiumViewer.dll" "/r:$binaryDirectory\itextsharp.dll" "/r:$binaryDirectory\EPPlus.dll" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'AdministrativeExtractionTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& $testExecutable (Join-Path $repositoryRoot 'tmp\extraction-tests')
if ($LASTEXITCODE -ne 0) { throw 'Extraction tests failed.' }
