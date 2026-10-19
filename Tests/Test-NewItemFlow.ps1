param([switch]$Child,[string]$EngineRoot)
$ErrorActionPreference='Stop'
if(-not $EngineRoot){$EngineRoot=Split-Path $PSScriptRoot -Parent}
if(-not $Child){
    $exe=[Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
    & $exe -NoProfile -File $PSCommandPath -Child -EngineRoot $EngineRoot
    if($LASTEXITCODE -ne 0){throw 'New-item flow fixture failed'}
    return
}
. (Join-Path $EngineRoot 'NewItemPlacement.ps1')
Add-Type @'
using System;
public static class DeskPagesHost {
 public static long NewItemDragGeneration {get;set;}
 public static bool CreationInputBusy {get;set;}
 public static bool Watching;public static void WatchNewItem(bool value){Watching=value;}
 public static int[] TakeDesktopContextClick(){return new int[]{620,330};}
}
public static class DeskPagesLayout {
 public static bool Editing;public static int Calls;public static string[] Ids;public static int X,Y;
 public static string FileId="disk:42";
 public class Gap {public int X=100,Y=120;}
 public static bool AutoArrange(object desktop){throw new InvalidOperationException("Arrange option unavailable on empty desktop");}
 public static int[] ContextGeometry(int x,int y){return new int[]{x,y,0,0,1920,1040};}
 public static bool IsLabelEditing(){return Editing;}
 public static string EditingId(object desktop){return "editing-pidl";}
 public static string CreationIdentity(string path){return FileId;}
 public static Gap Spacing(object desktop){return new Gap();}
 public static int Restore(object desktop,string[] ids,int[] xs,int[] ys){Calls++;Ids=ids;X=xs[0];Y=ys[0];return ids.Length;}
}
'@
function Assert($condition,$message){if(-not $condition){throw $message}}
$root=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-newflow-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try{
    $script:DpStore=$root;$script:DpDesktop=$root;$script:DpPublic=Join-Path $root 'Public';$script:DpSuspended=$false
    New-Item -ItemType Directory -Path $script:DpPublic | Out-Null
    $script:DpState=[PSCustomObject]@{Current=2}
    $script:icons=@()
    $document=[PSCustomObject]@{}
    $document | Add-Member ScriptMethod SelectedItems {return @()}
    $script:originalDesktop=[PSCustomObject]@{Document=$document}
    function Connect-DpDesktop {}
    function Read-DpVisibleIcons {return $script:icons}
    function Assert-DpPageView {param($Page,$Icons) if($Page -ne 2){throw 'Wrong page'}}
    function Set-DpFilePage {param($Page,$Commit) if($Page -ne 2 -or $Commit){throw 'Unexpected page switch'};$script:registered=$true}
    function Save-DpPageLayout {param($Page) if(-not $script:registered){throw 'Membership must be registered first'};$script:saved=$true}
    function Write-DpLog {param($Message)}
    Start-DpNewItemPlacement
    Assert ($null -ne $script:DpNewItemContext -and [DeskPagesHost]::Watching) 'Empty desktop did not arm'
    Update-DpNewItemPlacement
    Assert ([DeskPagesLayout]::Calls -eq 0) 'Moved an absent icon'
    $temporary=Join-Path $root 'New folder';New-Item -ItemType Directory -Path $temporary | Out-Null
    $script:icons=@([PSCustomObject]@{Id='editing-pidl';Key=$temporary;X=17;Y=2})
    [DeskPagesLayout]::Editing=$true
    Update-DpNewItemPlacement
    Assert ([DeskPagesLayout]::Calls -eq 0 -and $script:DpNewItemContext.SeenIdentity -eq 'disk:42') 'Identity not captured during inline edit'
    # Renaming changes both pathname and PIDL; click on blank space also clears selection.
    $final=Join-Path $root 'My renamed folder';Move-Item -LiteralPath $temporary -Destination $final
    $script:icons[0].Key=$final;$script:icons[0].Id='renamed-pidl'
    # Explorer relocates a provisional icon during native creation/rename.
    # Coordinate changes are NOT evidence of a mouse drag (device log repro).
    $script:icons[0].X=112;$script:icons[0].Y=124
    [DeskPagesLayout]::Editing=$false
    # Input generated while selecting New / ending rename is not cancellation.
    [DeskPagesHost]::NewItemDragGeneration++
    Update-DpNewItemPlacement
    Assert ($null -ne $script:DpNewItemContext) 'Menu/rename input incorrectly cancelled new-item placement'
    $script:DpNewItemContext.CandidateAt=[DateTime]::UtcNow.AddSeconds(-1)
    $script:DpNewItemContext.StableAt=[DateTime]::UtcNow.AddSeconds(-1)
    [DeskPagesHost]::CreationInputBusy=$true
    Update-DpNewItemPlacement
    Assert ([DeskPagesLayout]::Calls -eq 0 -and $null -ne $script:DpNewItemContext) 'Held mouse should defer, not cancel or position'
    [DeskPagesHost]::CreationInputBusy=$false
    Update-DpNewItemPlacement
    Assert ([DeskPagesLayout]::Calls -eq 1 -and [DeskPagesLayout]::Ids.Count -eq 1 -and [DeskPagesLayout]::Ids[0] -eq 'renamed-pidl') 'Did not position renamed/deselected folder once'
    $script:icons[0].X=[DeskPagesLayout]::X;$script:icons[0].Y=[DeskPagesLayout]::Y
    Update-DpNewItemPlacement
    Assert ($script:saved -and $null -eq $script:DpNewItemContext) 'Verified position not checkpointed'
    Assert (Test-Path -LiteralPath $final -PathType Container) 'Folder path changed during placement'
    Write-Host 'PASS: empty page with unavailable arrange option; observed edit PIDL; file identity survives rename/deselection; single new PIDL positioned and checkpointed'
    $anchorX=$script:icons[0].X;$anchorY=$script:icons[0].Y
    Start-DpNewItemPlacement
    $download=Join-Path $root 'background.txt';[IO.File]::WriteAllText($download,'unchanged contents');$hash=(Get-FileHash $download).Hash
    $script:icons+=@([PSCustomObject]@{Id='download';Key=$download;X=17;Y=2})
    Update-DpNewItemPlacement
    Assert ([DeskPagesLayout]::Calls -eq 1 -and -not $script:DpNewItemContext.SeenIdentity) 'Unselected background file was claimed'
    # A different file ID must not inherit the creation ticket.
    $script:DpNewItemContext.SeenIdentity='disk:original'
    [DeskPagesLayout]::FileId='disk:different'
    Update-DpNewItemPlacement
    Assert ([DeskPagesLayout]::Calls -eq 1) 'Different file consumed latched identity'
    $script:DpState.Current=3
    Update-DpNewItemPlacement
    Assert ($null -eq $script:DpNewItemContext -and [DeskPagesLayout]::Calls -eq 1) 'Placement crossed pages'
    Assert ($script:icons[0].X -eq $anchorX -and $script:icons[0].Y -eq $anchorY -and (Get-FileHash $download).Hash -eq $hash) 'Existing icon or file contents changed'
    Write-Host 'PASS: unrelated download, different file identity and page switch rejected; existing icon and file bytes preserved'
    $script:DpState.Current=2
    Start-DpNewItemPlacement
    [DeskPagesHost]::NewItemDragGeneration++
    Update-DpNewItemPlacement
    Assert ($null -ne $script:DpNewItemContext -and [DeskPagesLayout]::Calls -eq 1) 'Global mouse movement cancelled pending creation'
    $script:DpState.Current=3
    Update-DpNewItemPlacement
    Assert ($null -eq $script:DpNewItemContext) 'Page change did not cancel placement'
    Write-Host 'PASS: menu/rename/legacy drag signals cannot cancel creation; pressed button defers then placement completes on release'
}finally{Remove-Item -LiteralPath $root -Recurse -Force}
