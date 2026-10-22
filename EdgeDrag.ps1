# Real OLE file drag is received by a normal-user broker. This elevated agent only
# validates managed Desktop paths/identities and changes page membership/layout.
function Get-DpDragIdentity {param([string]$Path) [DeskPagesLayout]::CreationIdentity($Path)}
function Assert-DpDragItems {
    param([object[]]$Files,[int]$Source)
    if(-not $Files.Count -or $Files.Count -gt 256){throw 'Invalid drag selection.'}
    $seen=@{}
    foreach($file in $Files){
        $path=[IO.Path]::GetFullPath([string]$file.Path)
        if($seen.ContainsKey($path)){throw 'Duplicate drag item.'};$seen[$path]=$true
        if([IO.Path]::GetDirectoryName($path) -notin @($script:DpDesktop,$script:DpPublic)){throw 'Only managed Desktop items can cross pages.'}
        $entries=@($script:DpState.Items | Where-Object {$_.Path -ieq $path -and [int]$_.Page -eq $Source})
        if($entries.Count -ne 1 -or (Get-DpDragIdentity $path) -ne $file.Identity){throw 'Dragged file changed or no longer belongs to its original page.'}
    }
}
function Start-DpEdgeDrag {
    param([string[]]$Paths,[int]$GrabX,[int]$GrabY)
    if($script:DpSuspended -or $Paths.Count -lt 1 -or $Paths.Count -gt 256){throw 'Drag not available.'}
    Connect-DpDesktop
    $selected=@($script:originalDesktop.Document.SelectedItems() | ForEach-Object {[string]$_.Path})
    if($selected.Count -ne $Paths.Count){throw 'System/virtual icons cannot be transferred.'}
    foreach($path in $selected){if($path -notin $Paths){throw 'Drag selection differs from the Explorer selection.'}}
    $source=[int]$script:DpState.Current
    foreach($path in $Paths){if([IO.Path]::GetDirectoryName($path) -notin @($script:DpDesktop,$script:DpPublic)){throw 'Only physical Desktop paths are accepted.'}}
    # Register newly created/downloaded visible items on the source page before arming.
    Set-DpFilePage -Page $source
    $files=@(foreach($path in $Paths){[PSCustomObject]@{Path=$path;Identity=Get-DpDragIdentity $path}})
    Assert-DpDragItems -Files $files -Source $source
    foreach($entry in $script:DpState.Items){if($entry.Path -in $Paths -and (($entry.PSObject.Properties['Pinned'] -and $entry.Pinned) -or ((Get-Command Test-DpSecondaryKey -ErrorAction SilentlyContinue) -and (Test-DpSecondaryKey $entry.Path)))){throw 'Unpin shared items before dragging them between pages.'}}
    Save-DpVerifiedSourceLayout $source
    $sourceIcons=@([OriginalDesktopReader]::Read($script:originalDesktop))
    Assert-DpPageView -Page $source -Icons $sourceIcons
    $grab=[DeskPagesLayout]::ContextGeometry($GrabX,$GrabY)
    foreach($file in $files){
        $icon=@($sourceIcons | Where-Object {$_.Key -ieq $file.Path})
        if($icon.Count -ne 1){throw 'Source icon is not uniquely visible.'}
        $file | Add-Member -NotePropertyName SourceX -NotePropertyValue ([int]$icon[0].X)
        $file | Add-Member -NotePropertyName SourceY -NotePropertyValue ([int]$icon[0].Y)
    }
    if(Get-Command Get-DpWorkspaceSnapshot -ErrorAction SilentlyContinue){$script:DpDragUndoBefore=Get-DpWorkspaceSnapshot}
    $script:DpEdgeDrag=[PSCustomObject]@{GrabX=$grab[0];GrabY=$grab[1];Files=$files;Source=$source;Current=$source;Started=[DateTime]::UtcNow}
}
function Move-DpEdgeDragPage {
    param([ValidateSet(-1,1)][int]$Direction)
    $drag=$script:DpEdgeDrag
    if($null -eq $drag -or [int]$script:DpState.Current -ne $drag.Current){throw 'Drag session changed.'}
    Assert-DpDragItems -Files $drag.Files -Source $drag.Source
    $pages=[int[]]@($(if(Get-Command Get-DpNavigationPages -ErrorAction SilentlyContinue){Get-DpNavigationPages}elseif(Get-Command Get-DpActivePages -ErrorAction SilentlyContinue){Get-DpActivePages}else{$script:DpState.Pages | Sort-Object}))
    if($pages.Count -lt 2){throw 'Add a second page first.'}
    $index=[Array]::IndexOf($pages,[int]$script:DpState.Current)
    Set-DeskPage $pages[($index+$Direction+$pages.Count)%$pages.Count] -Direction (-$Direction)
    $drag.Current=[int]$script:DpState.Current
}
function Complete-DpEdgeDrop {
    param([int]$X,[int]$Y)
    $drag=$script:DpEdgeDrag
    if($null -eq $drag -or $script:DpSuspended -or [int]$script:DpState.Current -ne $drag.Current){throw 'Drop session changed.'}
    Assert-DpDragItems -Files $drag.Files -Source $drag.Source
    Save-DpVerifiedSourceLayout $drag.Current
    Save-DpSafetySnapshot
    $paths=@($drag.Files | ForEach-Object {$_.Path})
    $entries=@($script:DpState.Items | Where-Object {$_.Path -in $paths})
    $journal=Join-Path $script:DpStore 'transition.xml'
    Write-DpAtomicXml -Path $journal -Value ([PSCustomObject]@{From=$drag.Current;To=$drag.Current;At=[DateTime]::UtcNow;Operation='EdgeDrop'})
    try{
        if($script:hostReady){[DeskPagesHost]::BeginContentUpdate()}
        foreach($entry in $entries){$entry.Page=$drag.Current}
        Set-DpFilePage -Page $drag.Current -Commit:$false -RegisterNew:$false -PersistMembership:$false
        Refresh-DpDesktop;Restore-DpPageLayout $drag.Current
        $icons=@([OriginalDesktopReader]::Read($script:originalDesktop))
        Assert-DpPageView -Page $drag.Current -Icons $icons
        $geometry=[DeskPagesLayout]::ContextGeometry($X,$Y)
        $spacing=[DeskPagesLayout]::Spacing($script:originalDesktop)
        $snap=[DeskPagesLayout]::SnapToGrid($script:originalDesktop)
        $occupied=@($icons | Where-Object {$_.Key -notin $paths})
        $stationary=@($occupied | ForEach-Object {[PSCustomObject]@{Key=$_.Key;X=$_.X;Y=$_.Y}})
        $anchor=@($icons | Where-Object {$_.Key -in $paths})[0]
        if($null -eq $anchor){throw 'Drop grid is unavailable.'}
        $sourceGroup=[DeskPagesDragPlacement+Item[]]@(foreach($file in $drag.Files){[DeskPagesDragPlacement+Item]@{Key=$file.Path;X=$file.SourceX;Y=$file.SourceY}})
        $obstacles=[DeskPagesDragPlacement+Item[]]@(foreach($icon in $stationary){[DeskPagesDragPlacement+Item]@{Key=$icon.Key;X=$icon.X;Y=$icon.Y}})
        $plan=[DeskPagesDragPlacement]::Plan($geometry,$spacing.X,$spacing.Y,$snap,$anchor.X,$anchor.Y,$drag.GrabX,$drag.GrabY,$sourceGroup,$obstacles)
        if($null -eq $plan){throw 'There is no free space for this group without changing its arrangement.'}
        $toleranceX=2;$toleranceY=2
        if($snap){$toleranceX=$spacing.X/2;$toleranceY=$spacing.Y/2}
        $targets=@(foreach($position in $plan){
            $icon=@($icons | Where-Object {$_.Key -ieq $position.Key})
            if($icon.Count -ne 1){throw 'Dropped icon is not uniquely visible.'}
            [PSCustomObject]@{Id=$icon[0].Id;Key=$position.Key;X=$position.X;Y=$position.Y}
        })
        $null=[DeskPagesLayout]::Restore($script:originalDesktop,[string[]]@($targets | ForEach-Object {$_.Id}),[int[]]@($targets | ForEach-Object {$_.X}),[int[]]@($targets | ForEach-Object {$_.Y}))
        $verified=$false;$clock=[Diagnostics.Stopwatch]::StartNew()
        while($clock.ElapsedMilliseconds -lt 1200){
            $actual=@([OriginalDesktopReader]::Read($script:originalDesktop));$verified=$true
            foreach($target in $targets){$icon=@($actual | Where-Object {$_.Key -ieq $target.Key});if($icon.Count -ne 1 -or [Math]::Abs($icon[0].X-$target.X) -gt $toleranceX -or [Math]::Abs($icon[0].Y-$target.Y) -gt $toleranceY){$verified=$false;break}}
            if($verified){break};Start-Sleep -Milliseconds 20
        }
        if(-not $verified){throw 'Explorer placed a dropped icon beyond the nearest grid cell.'}
        foreach($old in $stationary){
            $now=@($actual | Where-Object {$_.Key -ieq $old.Key})
            if($now.Count -ne 1 -or [Math]::Abs($now[0].X-$old.X) -gt 2 -or [Math]::Abs($now[0].Y-$old.Y) -gt 2){throw 'An existing page icon moved; drop cancelled to protect the layout.'}
        }
        $placed=@($actual | Where-Object {$_.Key -in $paths})
        for($index=0;$index -lt $placed.Count;$index++){
            foreach($other in @($stationary)+@($placed | Select-Object -First $index)){
                if([Math]::Abs($placed[$index].X-$other.X) -lt ($spacing.X-2) -and [Math]::Abs($placed[$index].Y-$other.Y) -lt ($spacing.Y-2)){throw 'Dropped icons overlap; committed membership was preserved.'}
            }
        }
        $first=@($actual | Where-Object {$_.Key -ieq $drag.Files[0].Path})[0]
        foreach($file in $drag.Files){
            $item=@($actual | Where-Object {$_.Key -ieq $file.Path})[0]
            if([Math]::Abs(($item.X-$first.X)-($file.SourceX-$drag.Files[0].SourceX)) -gt 2 -or [Math]::Abs(($item.Y-$first.Y)-($file.SourceY-$drag.Files[0].SourceY)) -gt 2){throw 'Explorer changed the group arrangement; drop rolled back.'}
        }
        foreach($file in $drag.Files){if((Get-DpDragIdentity $file.Path) -ne $file.Identity){throw 'A dragged file changed during placement.'}}
        Assert-DpPageView -Page $drag.Current -Icons $actual
        # Durable target layout before manifest commit; extra old membership is ignored on rollback.
        Write-DpAtomicXml -Path (Join-Path $script:DpLayoutStore ("page-$($drag.Current).xml")) -Value ([PSCustomObject]@{Page=$drag.Current;SavedAt=[DateTime]::UtcNow;Icons=$actual})
        if(Get-Command Save-DpDragUndo -ErrorAction SilentlyContinue){Save-DpDragUndo}else{Save-DpState}
        [IO.File]::Delete($journal)
        Complete-DpPagePresentation
        Write-DpLog ('Edge drop committed: files='+$paths.Count+'; page='+$drag.Current+'; original group arrangement verified; actual Explorer positions accepted; original paths/content unchanged.')
    }catch{
        $failure=$_
        Write-DpLog ('Edge drop rolled back before acknowledgement: '+[string]$failure)
        if($script:hostReady){[DeskPagesHost]::AbortAnimation()}
        try{$script:DpState=Read-DpManifest $script:DpManifest;Resume-DpCommittedPage}catch{$script:DpSuspended=$true;Write-DpLog 'Edge drop rollback incomplete; saved state retained and further changes suspended.'}
        throw $failure
    }finally{$script:DpEdgeDrag=$null}
}
function Invoke-DpEdgeRequest {
    $request=[DeskPagesHost]::DragRequest
    if($null -eq $request){return}
    $success=$false
    Write-DpLog ('Edge drag request: '+$request.Kind)
    try{
        switch($request.Kind){
            'Arm' {Stop-DpNewItemPlacement;Start-DpEdgeDrag -Paths $request.Paths -GrabX $request.X -GrabY $request.Y}
            'Navigate' {Move-DpEdgeDragPage -Direction $request.X}
            'Drop' {Complete-DpEdgeDrop -X $request.X -Y $request.Y}
            'Cancel' {$script:DpEdgeDrag=$null}
            default {throw 'Unknown drag request.'}
        }
        $success=$true
    }catch{$script:DpEdgeDrag=$null;Write-DpLog ('Edge drag rejected: '+[string]$_)}
    finally{[DeskPagesHost]::CompleteDragRequest($success,[int]$script:DpState.Current)}
}
