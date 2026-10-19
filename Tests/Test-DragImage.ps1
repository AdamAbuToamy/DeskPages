$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$policy=[IO.File]::ReadAllText((Join-Path $root 'Native/DeskPagesDragImageSession.cs'))
$fixture=@'
public sealed class DeskPagesDragImageFixture:IDeskPagesDragImageSink {
 public readonly List<string> Calls=new List<string>();public object Original;public bool Fail;
 public void Enter(IntPtr h,object data,int x,int y,uint effect){if(Fail)throw new InvalidOperationException("fixture helper failure");if(!Object.ReferenceEquals(Original,data))throw new Exception("OLE data object changed");Calls.Add("enter:"+h.ToInt64());}
 public void Over(int x,int y,uint effect){Calls.Add("over:"+x+","+y);}
 public void Leave(){Calls.Add("leave");}
 public void Drop(object data,int x,int y,uint effect){if(effect!=0 || !Object.ReferenceEquals(Original,data))throw new Exception("Drop could trigger a filesystem move");Calls.Add("drop:none");}
}
'@
Add-Type -TypeDefinition ("using System.Collections.Generic;`n"+$policy+"`n"+$fixture)
$api=[DeskPagesDragImageFixture]::new();$data=[object]::new();$api.Original=$data;$script:visualErrors=@()
$session=[DeskPagesDragImageSession]::new($api,[Action[string]]{param($message) $script:visualErrors+=@($message)})
$edge=[IntPtr]1;$canvas=[IntPtr]2
$session.Enter($edge,$data,10,20,2);$session.Over($edge,11,22,2)
$session.Enter($canvas,$data,20,30,2)
$session.Leave($edge) # Late leave from the old strip must not hide the new canvas image.
$session.Over($canvas,400,300,2);$session.Drop($canvas,$data,400,300)
if(($api.Calls -join '|') -ne 'enter:1|over:11,22|leave|enter:2|over:400,300|drop:none'){throw ('Lost OLE image during target handoff: '+($api.Calls -join '|'))}
$session.Enter($canvas,$data,20,30,2);$session.Cancel();$count=$api.Calls.Count;$session.Cancel();$session.Over($canvas,50,60,2)
if($api.Calls.Count -ne $count){throw 'Cancelled drag still paints'}
$api.Fail=$true;$session.Enter($edge,$data,0,0,2)
if($script:visualErrors.Count -ne 1){throw 'Helper failure was not isolated'}
Write-Host 'PASS: original OLE object retained; strip-to-canvas image handoff; stale leave ignored; pointer updates; final NONE; cancel cleanup and visual helper failure isolation.'
if($env:OS -ne 'Windows_NT'){Write-Host 'SKIP: native Shell IDropTargetHelper rendering/COM roundtrip requires Windows.';return}
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$dataObject=[Windows.Forms.DataObject]::new()
$dataObject.SetData([Windows.Forms.DataFormats]::FileDrop,[string[]]@('C:\fixture.txt'))
if($dataObject -isnot [Runtime.InteropServices.ComTypes.IDataObject]){throw 'WinForms cannot forward original COM data interface'}
Write-Host 'PASS: Windows Forms OLE IDataObject interface is available to the native Shell helper. Physical drag visuals remain manual acceptance.'
