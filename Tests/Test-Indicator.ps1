$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if($env:OS -eq 'Windows_NT'){
 Add-Type -AssemblyName System.Windows.Forms
 Add-Type -AssemblyName System.Drawing
 Add-Type -Path @((Join-Path $root 'Native/DeskPagesIndicatorPolicy.cs'),(Join-Path $root 'Native/DeskPagesIndicator.cs')) -ReferencedAssemblies @('System.dll','System.Core.dll','System.Drawing','System.Windows.Forms')
}else{Add-Type -Path (Join-Path $root 'Native/DeskPagesIndicatorPolicy.cs')}
$policy=[DeskPagesIndicatorPolicy]::new()
if($policy.Opacity(10) -ne 0){throw 'Indicator visible without successful switch'}
$policy.Start(1000)
$previous=0
foreach($offset in 0..120){$value=$policy.Opacity(1000+$offset);if($value -lt $previous -or $value -gt 0.72){throw 'Invalid fade-in'};$previous=$value}
foreach($offset in @(120,500,849)){if([Math]::Abs($policy.Opacity(1000+$offset)-0.72) -gt 0.0001){throw 'Missing hold'}}
$previous=0.72
foreach($offset in 850..1150){$value=$policy.Opacity(1000+$offset);if($value -gt $previous -or $value -lt 0){throw 'Invalid fade-out'};$previous=$value}
if($policy.Opacity(2150) -ne 0){throw 'Indicator outlived deadline'}
$policy.Start(3000);$policy.Cancel();if($policy.Opacity(3100) -ne 0){throw 'Cancel failed'}
$policy.Start(4000);$policy.Start(4050);if($policy.Opacity(4050) -ne 0 -or $policy.Opacity(4169) -le 0){throw 'Latest switch timing not restarted'}
Write-Host 'PASS: indicator fade-in/hold/fade-out bounds, 1150 ms retirement, cancellation and latest-switch timing.'
$policy.Begin(10000)
foreach($now in @(10000,10001,12000,70000,3610000)){
 if(-not $policy.Pending -or $policy.Opacity($now) -ne 0.72){throw 'Pending feedback expired before completion.'}
}
$policy.Complete(4000000)
if($policy.Pending -or $policy.Opacity(4000299) -ne 0.72 -or $policy.Opacity(4000500) -ne 0){throw 'Completion hold/fade failed.'}
$policy.Begin(5000000);$policy.Fail(5001000)
if($policy.Pending -or $policy.Opacity(5001799) -le 0 -or $policy.Opacity(5002000) -ne 0){throw 'Failure feedback did not retire.'}
$policy.Begin(6000000);$policy.Cancel();$policy.Complete(6001000)
if($policy.Opacity(6001100) -ne 0){throw 'Late completion resurrected dismissed HUD.'}
$lifecycle=[DeskPagesFeedbackLifecycle]::new()
foreach($i in 1..10000){
 $lifecycle.Begin($i)
 if($lifecycle.TakeCompletion($false) -ne 0){throw 'Feedback completed before transaction commit.'}
 $lifecycle.Commit()
 if($lifecycle.TakeCompletion($true) -ne 0){throw 'Feedback completed before renderer retirement.'}
 if($lifecycle.TakeCompletion($false) -ne $i -or $lifecycle.TakeCompletion($false) -ne 0){throw 'Completion lost or dispatched twice.'}
 $lifecycle.Begin($i);$lifecycle.Cancel();$lifecycle.Commit()
 if($lifecycle.TakeCompletion($false) -ne 0){throw 'Cancelled lifecycle completed.'}
}
Write-Host 'PASS: indefinite immediate acknowledgement, completion-driven 300 ms hold/200 ms fade, error/dismissal retirement, 10000 commit+renderer races and cancellation checks.'

if($env:OS -ne 'Windows_NT'){Write-Host 'SKIP: native click-through, foreground preservation and real display rendering need Windows.';return}
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;
public static class DeskPagesIndicatorTestNative {
 [DllImport("user32.dll")]public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]public static extern int GetWindowLongW(IntPtr h,int index);
}
'@
$hud=[DeskPagesIndicator]::new()
try{
 $before=[DeskPagesIndicatorTestNative]::GetForegroundWindow();$hud.Present(3);[Windows.Forms.Application]::DoEvents()
 if([DeskPagesIndicatorTestNative]::GetForegroundWindow() -ne $before){throw 'HUD stole focus'}
 $style=[DeskPagesIndicatorTestNative]::GetWindowLongW($hud.Handle,-20)
 if(($style -band 0x080800A0) -ne 0x080800A0 -or $hud.ShowInTaskbar){throw 'HUD input isolation styles missing'}
 $clock=[Diagnostics.Stopwatch]::StartNew()
 while($clock.ElapsedMilliseconds -lt 1400){[Windows.Forms.Application]::DoEvents();Start-Sleep -Milliseconds 10}
 if($hud.Visible){throw 'HUD did not retire'}
 $hud.Present(99);$hud.Dismiss();if($hud.Visible){throw 'HUD remained visible before capture'}
 Write-Host 'PASS: Windows HUD preserves foreground, has layered/noactivate/transparent/toolwindow styles, auto-hides and dismisses.'
}finally{$hud.Dispose()}
