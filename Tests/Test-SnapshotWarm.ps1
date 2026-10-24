$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. (Join-Path $root 'SnapshotCache.ps1')
Add-Type @'
public static class DeskPagesHost {
 public static bool SnapshotCacheEnabled=true;
 public static int Captures;
 public static bool NeedsSnapshotCapture(string key){return false;}
 public static void SnapshotSceneContext(string context){}
 public static void WarmSnapshot(string key){if(key!="2:SAME")throw new System.Exception("Wrong key");Captures++;}
}
'@
$temp=Join-Path ([IO.Path]::GetTempPath()) ('dp-warm-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try{
 $script:DpStore=$temp;$script:DpSuspended=$false
 $script:DpState=[PSCustomObject]@{Current=2;Items=@();SystemIconBackup=@()}
 function Save-DpVerifiedSourceLayout {param($Page)if($Page -ne 2){throw 'Wrong page'}}
 function Get-DpSnapshotKey {param($Page)return '2:SAME'}
 # Repeated actual warm requests with unchanged geometry must replace pixels.
 foreach($i in 1..100){Warm-DpCurrentSnapshot}
 if([DeskPagesHost]::Captures -ne 100){throw 'An unchanged layout suppressed image refresh.'}
 $script:DpSuspended=$true;Warm-DpCurrentSnapshot;$script:DpSuspended=$false
 New-Item -ItemType File -Path (Join-Path $temp 'transition.xml') | Out-Null
 Warm-DpCurrentSnapshot
 Remove-Item -LiteralPath (Join-Path $temp 'transition.xml')
 [DeskPagesHost]::SnapshotCacheEnabled=$false;Warm-DpCurrentSnapshot
 if([DeskPagesHost]::Captures -ne 100){throw 'Suspended/incomplete/direct-capture mode warmed an image.'}
 Write-Host 'PASS: event-requested unchanged-layout snapshots refresh pixels; suspended, journaled and direct-capture sessions remain fenced. Capture adapter fixture, not Explorer rendering.'
}finally{Remove-Item -LiteralPath $temp -Recurse -Force}
