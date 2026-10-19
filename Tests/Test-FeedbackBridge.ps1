$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. (Join-Path $root 'Layouts.ps1')
Add-Type @'
using System;using System.Collections.Generic;
public static class DeskPagesHost {
 public static List<string> Events=new List<string>();
 public static void BeginPageFeedback(int page,int direction){Events.Add("accepted:"+page+":"+direction);}
 public static void CompletePageFeedback(){Events.Add("committed");}
 public static void FailPageFeedback(){Events.Add("failed");}
}
'@
$script:hostReady=$true
$script:DpState=[PSCustomObject]@{Current=1}
function Invoke-DpPageTransaction {
 param($Page,$Animate,$Direction)
 [DeskPagesHost]::Events.Add('transaction')
 if($script:reject){throw 'Injected transaction failure'}
}
foreach($animated in @($true,$false)){
 [DeskPagesHost]::Events.Clear();$script:reject=$false
 Set-DeskPage -Page 2 -Animate $animated -Direction -1
 if(([DeskPagesHost]::Events -join '|') -ne 'accepted:2:-1|transaction|committed'){throw 'Feedback did not precede transaction work.'}
}
[DeskPagesHost]::Events.Clear();$script:reject=$true
$failed=$false;try{Set-DeskPage 2}catch{if($_ -match 'Injected transaction failure'){$failed=$true}else{throw}}
if(-not $failed -or ([DeskPagesHost]::Events -join '|') -ne 'accepted:2:0|transaction|failed'){throw 'Failure swallowed or success feedback emitted.'}
[DeskPagesHost]::Events.Clear();$script:reject=$false;Set-DeskPage 1
if(([DeskPagesHost]::Events -join '|') -ne 'transaction'){throw 'Same-page request flashed feedback.'}
[DeskPagesHost]::Events.Clear();$script:hostReady=$false;Set-DeskPage 2
if(([DeskPagesHost]::Events -join '|') -ne 'transaction'){throw 'Headless transaction acquired feedback dependency.'}
Write-Host 'PASS: actual Set-DeskPage wrapper acknowledges before transaction work, preserves parameters and exceptions, handles no-animation, same-page and hostless paths.'
