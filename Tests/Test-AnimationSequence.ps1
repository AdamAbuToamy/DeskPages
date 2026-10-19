param([switch]$Child)
$ErrorActionPreference='Stop'
# Use a fresh process: the normal Check command already loaded the real host.
if(-not $Child){
    $exe=[Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
    & $exe -NoProfile -File $PSCommandPath -Child
    if($LASTEXITCODE -ne 0){throw 'Animation transaction sequence regression failed.'}
    return
}
$base=Split-Path $PSScriptRoot -Parent
. (Join-Path $base 'StateEngine.ps1')
Add-Type @'
using System;
using System.Collections.Generic;
public static class DeskPagesAnimation {public static bool IsVisible;}
public static class DeskPagesHost {
 public static List<string> Events=new List<string>();
 public static void BeginAnimation(int direction){Events.Add("source-picture");DeskPagesAnimation.IsVisible=true;}
 public static void ConfirmAnimationTarget(int count){Events.Add("confirmed:"+count);}
 public static void FinishAnimation(){Events.Add("play");DeskPagesAnimation.IsVisible=false;}
 public static void AbortAnimation(){Events.Add("abort");DeskPagesAnimation.IsVisible=false;}
}
'@
$root=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-sequence-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try {
    function Write-DpLog {param($Message)}
    function Save-DpPageLayout {param($Page) [DeskPagesHost]::Events.Add('source-layout')}
    function Set-DpFilePage {param($Page,$Commit,$RegisterNew) [DeskPagesHost]::Events.Add('visibility:'+ $Page)}
    function Set-DpSystemIcons {param($Page) return $false}
    function Refresh-DpDesktop {}
    function Restore-DpPageLayout {param($Page) [DeskPagesHost]::Events.Add('layout:'+ $Page)}
    function Confirm-DpAnimationView {param($Page)
        [DeskPagesHost]::Events.Add('verify-target')
        if($script:rejectTarget){throw 'Target includes a source icon or lacks an expected target icon.'}
        for($i=0;$i -lt $script:targetCount;$i++){[PSCustomObject]@{Key=('target-'+$i)}}
    }
    foreach($case in @('Populated','Empty','MixedRejected')){
        $script:DpStore=$root;$script:DpManifest=Join-Path $root 'pages.xml'
        $script:DpSuspended=$false;$script:hostReady=$true
        $script:DpState=[PSCustomObject]@{Current=1;Pages=@(1,2);Items=@()}
        $script:targetCount=3;if($case -eq 'Empty'){$script:targetCount=0}
        $script:rejectTarget=$case -eq 'MixedRejected'
        [DeskPagesHost]::Events.Clear();Save-DpState
        $failed=$false;try {Invoke-DpPageTransaction -Page 2} catch {$failed=$true}
        $events=@([DeskPagesHost]::Events)
        if($case -eq 'MixedRejected'){
            if(-not $failed -or 'play' -in $events -or $script:DpState.Current -ne 1 -or 'abort' -notin $events){throw 'Invalid target was displayed or committed.'}
        }else{
            $expected=@('source-layout','visibility:1','source-picture','visibility:2','layout:2','verify-target',('confirmed:'+$script:targetCount),'play')
            if($failed -or ($events -join '|') -ne ($expected -join '|') -or $script:DpState.Current -ne 2){throw ('Wrong sequence: '+($events -join '|'))}
        }
        Write-Host ('PASS: animated transaction '+$case)
    }
    Write-Host 'Simulated host/view adapters; no actual screenshot, Windows paint or physical display verification.'
}finally{Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue}
