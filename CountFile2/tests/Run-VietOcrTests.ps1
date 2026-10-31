param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug', [switch]$RealOcr)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$builder = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
& $builder (Join-Path $root 'CountFile2.sln') /t:Build "/p:Configuration=$Configuration" /verbosity:quiet /nologo /nr:false
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$bin = Join-Path $root "CountFile2\bin\$Configuration"
$exe = Join-Path $bin 'VietOcrTests.exe'
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe') /nologo /target:exe /platform:x86 "/out:$exe" "/r:$bin\CountFile2.exe" "/r:$bin\itextsharp.dll" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'VietOcrTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
Copy-Item -LiteralPath (Join-Path $bin 'CountFile2.exe.config') -Destination "$exe.config" -Force
$testRoot = Join-Path $root ('tmp\vietocr-tests\run-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$python = Join-Path $root '.vietocr-venv\Scripts\python.exe'
& $python (Join-Path $PSScriptRoot 'test_vietocr_geometry.py')
if ($LASTEXITCODE -ne 0) { throw 'OCR geometry tests failed.' }
& $python (Join-Path $PSScriptRoot 'test_vietocr_page_number.py')
if ($LASTEXITCODE -ne 0) { throw 'Page number tests failed.' }
if ($RealOcr) {
    & $exe $testRoot $python (Join-Path $bin 'VietOcr\worker.py') real
} else {
    $fixture = Join-Path $testRoot 'fake_worker.py'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fake_vietocr_worker.py') -Destination $fixture
    & $exe $testRoot $python $fixture
}
if ($LASTEXITCODE -ne 0) { throw 'VietOCR tests failed.' }
