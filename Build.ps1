param([string]$OutputPath = (Join-Path $PSScriptRoot 'bin\MemoryCompression.exe'))

$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'This application requires Windows.' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler not found. Install .NET Framework 4.8.' }
$iconPath = Join-Path $PSScriptRoot 'assets\app.ico'
if (-not (Test-Path -LiteralPath $iconPath)) { throw 'Application icon not found: assets\app.ico' }
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Path (Split-Path -Parent $OutputPath) -Force | Out-Null
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /utf8output /codepage:65001 `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll /reference:System.Management.dll /reference:System.Web.Extensions.dll `
    "/win32icon:$iconPath" "/out:$OutputPath" (Join-Path $PSScriptRoot 'src\MemoryCompression.cs') (Join-Path $PSScriptRoot 'src\PreviewCleanup.cs') (Join-Path $PSScriptRoot 'src\GentleEngine.cs')
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Get-Item -LiteralPath $OutputPath | Select-Object FullName,Length
