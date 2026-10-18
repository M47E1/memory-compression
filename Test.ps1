param(
    [string]$OutputDirectory,
    [switch]$Browser,
    [string]$PlaywrightPath,
    [string]$ChromiumPath,
    [string]$RealNextProject
)

$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'The Windows runtime tests require Windows.' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot ('test-output\' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fffffff')) }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
& (Join-Path $PSScriptRoot 'Build.ps1')
& node --test (Join-Path $PSScriptRoot 'tests\ports.test.cjs') (Join-Path $PSScriptRoot 'tests\release.test.cjs')
if ($LASTEXITCODE -ne 0) { throw 'JavaScript tests failed.' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$controller = Join-Path $OutputDirectory 'PreviewCleanupTests.exe'
& $compiler /nologo /target:exe /utf8output /codepage:65001 /reference:System.Core.dll /reference:System.Management.dll /reference:System.Web.Extensions.dll `
    "/out:$controller" (Join-Path $PSScriptRoot 'src\PreviewCleanup.cs') (Join-Path $PSScriptRoot 'tests\PreviewCleanupTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Controller test compilation failed.' }
& $controller (Join-Path $OutputDirectory 'controller')
if ($LASTEXITCODE -ne 0) { throw 'Controller tests failed.' }
$adversarial = Join-Path $OutputDirectory 'AdversarialTests.exe'
& $compiler /nologo /target:exe /utf8output /codepage:65001 /reference:System.Core.dll /reference:System.Management.dll /reference:System.Web.Extensions.dll `
    "/out:$adversarial" (Join-Path $PSScriptRoot 'src\PreviewCleanup.cs') (Join-Path $PSScriptRoot 'tests\AdversarialTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Adversarial test compilation failed.' }
& $adversarial
if ($LASTEXITCODE -ne 0) { throw 'Adversarial tests failed.' }
$stub = Join-Path $OutputDirectory 'OptimizerStub.exe'
& $compiler /nologo /target:exe "/out:$stub" (Join-Path $PSScriptRoot 'tests\OptimizerStub.cs')
if ($LASTEXITCODE -ne 0) { throw 'Optional-engine test helper compilation failed.' }
$runtime = Join-Path $OutputDirectory 'RuntimeTests.exe'
& $compiler /nologo /target:exe /main:RuntimeTests /utf8output /codepage:65001 `
    /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
    /reference:System.Management.dll /reference:System.Web.Extensions.dll "/out:$runtime" `
    (Join-Path $PSScriptRoot 'src\MemoryCompression.cs') (Join-Path $PSScriptRoot 'src\PreviewCleanup.cs') (Join-Path $PSScriptRoot 'tests\RuntimeTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Runtime test compilation failed.' }
$runtimeOut = Join-Path $OutputDirectory 'runtime.stdout.log'
$runtimeError = Join-Path $OutputDirectory 'runtime.stderr.log'
$process = Start-Process -FilePath $runtime -ArgumentList @(('"' + $OutputDirectory + '"'),('"' + $stub + '"')) `
    -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput $runtimeOut -RedirectStandardError $runtimeError
Get-Content -LiteralPath $runtimeOut
if ($process.ExitCode -ne 0) { Get-Content -LiteralPath $runtimeError; throw 'Runtime tests failed.' }
foreach ($mode in @('json', 'offscreen')) {
    $report = Join-Path $OutputDirectory ("ui-$mode.json")
    $process = Start-Process -FilePath (Join-Path $PSScriptRoot 'bin\MemoryCompression.exe') `
        -ArgumentList @("--test-$mode", ('"' + $report + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "UI $mode test failed." }
    $state = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if ($state.optimizer_configured -or $state.optimizer_started -or $state.preview_bridge_started -or -not $state.preview_cleanup_enabled) {
        throw 'Default UI unexpectedly configured an optimizer or started a live worker during the render test.'
    }
}
if ($Browser) {
    $memory = Get-CimInstance Win32_PerfFormattedData_PerfOS_Memory
    if ($memory.AvailableBytes -lt 2GB -or $memory.PercentCommittedBytesInUse -ge 80 -or ($memory.CommitLimit-$memory.CommittedBytes) -lt 6GB) {
        throw 'Not enough free memory for a browser test; no browser was started.'
    }
    if (-not $PlaywrightPath) { $PlaywrightPath = Join-Path $PSScriptRoot 'node_modules\playwright' }
    if (-not (Test-Path -LiteralPath (Join-Path $PlaywrightPath 'package.json'))) { throw 'Run npm install first, or pass -PlaywrightPath.' }
    $env:PREVIEW_TEST_PLAYWRIGHT = (Resolve-Path -LiteralPath $PlaywrightPath).Path
    if ($ChromiumPath) { $env:PREVIEW_TEST_CHROMIUM = (Resolve-Path -LiteralPath $ChromiumPath).Path }
    else { Remove-Item Env:PREVIEW_TEST_CHROMIUM -ErrorAction SilentlyContinue }
    if ($RealNextProject) { $env:PREVIEW_TEST_NEXT_PROJECT = (Resolve-Path -LiteralPath $RealNextProject).Path }
    else { Remove-Item Env:PREVIEW_TEST_NEXT_PROJECT -ErrorAction SilentlyContinue }
    & node (Join-Path $PSScriptRoot 'tests\browser.cjs') (Join-Path $OutputDirectory 'browser')
    if ($LASTEXITCODE -ne 0) { throw 'Browser tests failed.' }
}
Write-Output "TEST_OUTPUT=$OutputDirectory"
