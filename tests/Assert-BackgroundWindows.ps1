param([Parameter(Mandatory=$true)][string]$ProfileDirectory)

$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class PreviewTestWindows {
  public delegate bool EnumProc(IntPtr handle, IntPtr data);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr data);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr handle, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr handle, StringBuilder name, int count);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr handle, out Rect rect);
  [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
}
'@
$all = @(Get-CimInstance Win32_Process)
$ids = New-Object 'System.Collections.Generic.HashSet[int]'
foreach ($process in $all) {
  if ($process.Name -match '^chrome.*\.exe$' -and ([string]$process.CommandLine).Contains($ProfileDirectory)) { [void]$ids.Add([int]$process.ProcessId) }
}
if ($ids.Count -eq 0) { throw 'The task-owned Chromium root could not be identified.' }
do {
  $added = $false
  foreach ($process in $all) { if ($ids.Contains([int]$process.ParentProcessId) -and $ids.Add([int]$process.ProcessId)) { $added = $true } }
} while ($added)
$bad = New-Object 'System.Collections.Generic.List[string]'
$seen = New-Object 'System.Collections.Generic.List[string]'
$callback = [PreviewTestWindows+EnumProc]{
  param([IntPtr]$handle, [IntPtr]$data)
  [uint32]$owner = 0
  [void][PreviewTestWindows]::GetWindowThreadProcessId($handle, [ref]$owner)
  if ($ids.Contains([int]$owner)) {
    $name = New-Object System.Text.StringBuilder 256
    [void][PreviewTestWindows]::GetClassName($handle, $name, 256)
    if ($name.ToString() -eq 'Chrome_WidgetWin_1') {
      $rect = New-Object PreviewTestWindows+Rect
      [void][PreviewTestWindows]::GetWindowRect($handle, [ref]$rect)
      $seen.Add("$owner $($rect.Left),$($rect.Top),$($rect.Right),$($rect.Bottom)")
      if ($rect.Right -gt $rect.Left -and $rect.Bottom -gt $rect.Top -and ($rect.Left -ne -10000 -or $rect.Top -ne -10000)) { $bad.Add($seen[$seen.Count-1]) }
    }
  }
  return $true
}
[void][PreviewTestWindows]::EnumWindows($callback, [IntPtr]::Zero)
if ($bad.Count) { throw ('Test browser window was not off-screen: ' + ($bad -join '; ')) }
[pscustomobject]@{ TaskOwnedProcesses=$ids.Count; CheckedWindows=@($seen.ToArray()); Offscreen=$true }|ConvertTo-Json
