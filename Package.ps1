param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts'))

$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Build.ps1')
$version = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'package.json') -Raw | ConvertFrom-Json).version
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
$stage = Join-Path $OutputDirectory ('package-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fffffff'))
if (Test-Path -LiteralPath $stage) { throw 'Package staging directory already exists.' }
New-Item -ItemType Directory -Path $stage | Out-Null
$sourceFiles = @(
    '.gitignore', 'LICENSE', 'NOTICE', 'README.md', 'AUDIT.md', 'Build.ps1', 'Test.ps1', 'Package.ps1', 'package.json', 'package-lock.json',
    'assets\README.md', 'assets\cover-github.png', 'assets\cover-portfolio.webp', 'assets\app-window.png',
    'src\MemoryCompression.cs', 'src\PreviewCleanup.cs',
    'browser-extension\manifest.json', 'browser-extension\background.js', 'browser-extension\ports.js',
    'browser-extension\popup.html', 'browser-extension\popup.js',
    'tests\PreviewCleanupTests.cs', 'tests\AdversarialTests.cs', 'tests\RuntimeTests.cs', 'tests\OptimizerStub.cs',
    'tests\ports.test.cjs', 'tests\release.test.cjs', 'tests\browser.cjs', 'tests\Assert-BackgroundWindows.ps1'
)
function Copy-PackageFile([string]$source, [string]$destination) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination
}
$sourceStage = Join-Path $stage 'source'
foreach ($file in $sourceFiles) { Copy-PackageFile (Join-Path $PSScriptRoot $file) (Join-Path $sourceStage $file) }
$portableStage = Join-Path $stage 'portable'
foreach ($file in @('LICENSE', 'NOTICE', 'README.md', 'AUDIT.md') + @($sourceFiles | Where-Object { $_ -like 'browser-extension\*' -or $_ -like 'assets\*' })) {
    Copy-PackageFile (Join-Path $PSScriptRoot $file) (Join-Path $portableStage $file)
}
Copy-PackageFile (Join-Path $PSScriptRoot 'bin\MemoryCompression.exe') (Join-Path $portableStage 'MemoryCompression.exe')
foreach ($kind in @('source','portable')) {
    $zip = Join-Path $OutputDirectory ("memory-compression-$version-windows-$kind.zip")
    if (Test-Path -LiteralPath $zip) { throw "Package already exists; choose a new output directory: $zip" }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $stage $kind), $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
    Get-Item -LiteralPath $zip | Select-Object FullName,Length
}
