$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
. (Join-Path $base 'StateEngine.ps1');. (Join-Path $base 'Layouts.ps1');. (Join-Path $base 'Visibility.ps1')
Add-Type @'
using System;
public static class DpOriginalViewRefresh {
 public static string[] Paths;public static int Calls;
 public static void NotifyAttributes(string[] paths){Paths=paths;Calls++;}
}
public static class OriginalDesktopReader {
 public static object[] Icons;public static object[] Read(object desktop){return Icons;}
}
'@
function Assert($Value,$Message){if(-not $Value){throw $Message}}
$root=Join-Path ([IO.Path]::GetTempPath()) ('dp-page-view-'+[guid]::NewGuid().ToString('N'))
try{
 New-Item -ItemType Directory $root | Out-Null
 $a=Join-Path $root 'a.txt';$b=Join-Path $root 'b.txt';[IO.File]::WriteAllText($a,'a');[IO.File]::WriteAllText($b,'b')
 $script:DpState=[PSCustomObject]@{Items=@([PSCustomObject]@{Path=$a;Page=1},[PSCustomObject]@{Path=$b;Page=2});SystemIconBackup=@()}
 $script:DpVisibilityChangedPaths=@($a,$b);$script:refreshes=0
 function Refresh-DpDesktop {$script:refreshes++;[OriginalDesktopReader]::Icons=@([PSCustomObject]@{Key=$b})}
 [OriginalDesktopReader]::Icons=@([PSCustomObject]@{Key=$b})
 Update-DpPageView 2 $false
 Assert ($script:refreshes -eq 0 -and [DpOriginalViewRefresh]::Calls -eq 1) 'A ready ordinary page invoked full Explorer refresh.'
 Assert (([DpOriginalViewRefresh]::Paths -join ';') -eq (@($a,$b) -join ';')) 'Only changed attribute paths should be notified.'
 Update-DpPageView 2 $true;Assert ($script:refreshes -eq 1) 'System-icon registry changes did not refresh.'
 [OriginalDesktopReader]::Icons=@([PSCustomObject]@{Key=$a},[PSCustomObject]@{Key=$b})
 Update-DpPageView 2 $false;Assert ($script:refreshes -eq 2) 'A stale/mixed view was accepted instead of using the refresh fallback.'
 [OriginalDesktopReader]::Icons=@();$script:DpVisibilityChangedPaths=@()
 Update-DpPageView 3 $false;Assert ($script:refreshes -eq 2) 'An empty valid page was needlessly refreshed.'
 Write-Host 'PASS: ordinary/empty verified pages avoid full refresh; changed paths are notified; system-icon changes and stale/mixed views retain the full-refresh fallback. Explorer rendering is mocked.'
}finally{Remove-Item -LiteralPath $root -Recurse -Force}
