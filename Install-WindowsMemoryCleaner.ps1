param([string]$ApplicationDirectory = $PSScriptRoot)

$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Windows Memory Cleaner requires Windows.' }
$ApplicationDirectory = [IO.Path]::GetFullPath($ApplicationDirectory)
$engineDirectory = Join-Path $ApplicationDirectory 'engines\WindowsMemoryCleaner'
$destination = Join-Path $engineDirectory 'WinMemoryCleaner.exe'
$release = 'https://github.com/IgorMundstein/WinMemoryCleaner/releases/download/3.0.8'
$expectedDigest = '8b68d56c6ee28740f76513f21b21f5a1018f0ef291467b3f3d39d43daf0c0f2f'

function Assert-OfficialEngine([string]$Path) {
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedDigest) {
        throw 'The executable does not match the official 3.0.8 release. It was not installed or executed.'
    }
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(^|,\s*)CN=SignPath Foundation(,|$)') {
        throw ('The official signature could not be verified: ' + $signature.Status + '. It was not installed or executed.')
    }
}

New-Item -ItemType Directory -Path $engineDirectory -Force | Out-Null
if (Test-Path -LiteralPath $destination) {
    Assert-OfficialEngine $destination
    Write-Output 'The verified official Windows Memory Cleaner 3.0.8 is already installed.'
    return
}
$download = Join-Path $engineDirectory ('WinMemoryCleaner-' + [guid]::NewGuid().ToString('N') + '.download')
$license = Join-Path $engineDirectory 'LICENSE'
Invoke-WebRequest -UseBasicParsing -Uri ($release + '/WinMemoryCleaner.exe') -OutFile $download
Assert-OfficialEngine $download
Invoke-WebRequest -UseBasicParsing -Uri 'https://raw.githubusercontent.com/IgorMundstein/WinMemoryCleaner/3.0.8/LICENSE' -OutFile $license
# Never replace an existing engine, change execution policy, or bypass UAC.
[IO.File]::Move($download, $destination)
Write-Output ('Installed the verified official engine: ' + $destination)
Write-Output 'Restart Memory Compression. Memory cleanup remains manual and low-priority-only.'
Write-Output 'No engine was executed and no service or startup task was installed.'
