$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Add-Type -Path (Join-Path $root 'Native/DeskPagesIndicatorPolicy.cs')
$policy=[DeskPagesIndicatorPolicy]::new()
if($policy.Opacity(10) -ne 0){throw 'Indicator visible without successful switch'}
$policy.Start(1000)
$previous=0
foreach($offset in 0..120){$value=$policy.Opacity(1000+$offset);if($value -lt $previous -or $value -gt 0.86){throw 'Invalid fade-in'};$previous=$value}
foreach($offset in @(120,500,849)){if([Math]::Abs($policy.Opacity(1000+$offset)-0.86) -gt 0.0001){throw 'Missing hold'}}
$previous=0.86
foreach($offset in 850..1150){$value=$policy.Opacity(1000+$offset);if($value -gt $previous -or $value -lt 0){throw 'Invalid fade-out'};$previous=$value}
if($policy.Opacity(2150) -ne 0){throw 'Indicator outlived deadline'}
$policy.Start(3000);$policy.Cancel();if($policy.Opacity(3100) -ne 0){throw 'Cancel failed'}
$policy.Start(4000);$policy.Start(4050);if($policy.Opacity(4050) -ne 0 -or $policy.Opacity(4169) -le 0){throw 'Latest switch timing not restarted'}
Write-Host 'PASS: indicator fade-in/hold/fade-out bounds, 1150 ms retirement, cancellation and latest-switch timing.'
if($env:OS -ne 'Windows_NT'){Write-Host 'SKIP: native click-through, foreground preservation and real display rendering need Windows.';return}
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -Path (Join-Path $root 'Native/DeskPagesIndicator.cs') -ReferencedAssemblies @('System.Drawing','System.Windows.Forms')
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
