param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [ValidateSet('x86', 'x64')][string]$Architecture = 'x86'
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$buildTool = & $vswherePath -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $buildTool) { throw 'Visual Studio MSBuild was not found.' }
& $buildTool (Join-Path $repositoryRoot 'CountFile2.sln') /t:Build "/p:Configuration=$Configuration" /verbosity:quiet /nologo /nr:false
if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
$binaryDirectory = Join-Path $repositoryRoot "CountFile2\bin\$Configuration"
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$testExecutable = Join-Path $binaryDirectory "ConversionIntegrationTests-$Architecture.exe"
& $compilerPath /nologo /target:exe "/platform:$Architecture" "/out:$testExecutable" "/r:$binaryDirectory\CountFile2.exe" "/r:$binaryDirectory\PdfiumViewer.dll" "/r:$binaryDirectory\itextsharp.dll" /r:System.Drawing.dll /r:System.Windows.Forms.dll (Join-Path $PSScriptRoot 'ConversionIntegrationTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& $testExecutable (Join-Path $repositoryRoot 'tmp\conversion-tests')
if ($LASTEXITCODE -ne 0) { throw 'Conversion tests failed.' }
