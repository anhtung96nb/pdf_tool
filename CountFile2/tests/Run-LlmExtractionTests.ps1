param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$buildTool = & $vswherePath -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
& $buildTool (Join-Path $repositoryRoot 'CountFile2.sln') /t:Build "/p:Configuration=$Configuration" /verbosity:quiet /nologo /nr:false
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$binaryDirectory = Join-Path $repositoryRoot "CountFile2\bin\$Configuration"
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$testExecutable = Join-Path $binaryDirectory 'LlmExtractionTests.exe'
& $compilerPath /nologo /target:exe /platform:x64 "/out:$testExecutable" "/r:$binaryDirectory\CountFile2.exe" "/r:$binaryDirectory\PdfiumViewer.dll" "/r:$binaryDirectory\itextsharp.dll" "/r:$binaryDirectory\EPPlus.dll" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Net.Http.dll (Join-Path $PSScriptRoot 'LlmExtractionTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
$testTemp = Join-Path $repositoryRoot 'tmp\llm-test-temp'
New-Item -ItemType Directory -Path $testTemp -Force | Out-Null
$env:TEMP = $testTemp
$env:TMP = $testTemp
$fixtureFolder = Join-Path $testTemp ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureFolder -Force | Out-Null
$workerFixture = Join-Path $fixtureFolder 'worker.py'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fake_vietocr_worker.py') -Destination $workerFixture
& $testExecutable (Join-Path $repositoryRoot 'tmp\llm-tests') (Join-Path $repositoryRoot '.vietocr-venv\Scripts\python.exe') $workerFixture
if ($LASTEXITCODE -ne 0) { throw 'LLM extraction tests failed.' }
