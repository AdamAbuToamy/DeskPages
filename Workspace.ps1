# Data-only workspace operations. Metadata snapshots never contain user file bytes.
function Copy-DpData {param($Value) [Management.Automation.PSSerializer]::Deserialize([Management.Automation.PSSerializer]::Serialize($Value,100))}
function Initialize-DpWorkspace {
    if(-not $script:DpState.PSObject.Properties['Workspace']){
        $script:DpState | Add-Member -NotePropertyName Workspace -NotePropertyValue ([PSCustomObject]@{Schema=1;Archived=@();Locked=@();Order=@($script:DpState.Pages);Inbox=0;Rules=@();Undo=@();Redo=@();Language='en';ReduceMotion=$false;WheelThreshold=40;PointerThreshold=350;EdgeWidth=48;EdgeDwell=320;MonitorKey='';Names=@()})
    }
    if(-not $script:DpState.Workspace.PSObject.Properties['SystemPinned']){$script:DpState.Workspace | Add-Member -NotePropertyName SystemPinned -NotePropertyValue @()}
    if(-not $script:DpState.Workspace.PSObject.Properties['Input']){
        $inputFlags=@($true,$false,$true,$true,$true,$true)
        $path=Join-Path $script:DpStore 'input-settings.txt'
        if(Test-Path -LiteralPath $path){$lines=@(Get-Content -LiteralPath $path);$indices=@(0,1,2,3,5,6);for($i=0;$i -lt 6;$i++){$value=$false;if($indices[$i] -lt $lines.Count -and [bool]::TryParse($lines[$indices[$i]],[ref]$value)){$inputFlags[$i]=$value}}}
        $script:DpState.Workspace | Add-Member -NotePropertyName Input -NotePropertyValue $inputFlags
    }
    if(-not $script:DpState.Workspace.PSObject.Properties['PrimaryOnly']){$script:DpState.Workspace | Add-Member -NotePropertyName PrimaryOnly -NotePropertyValue $true}
    if(-not $script:DpState.Workspace.PSObject.Properties['SecondaryIcons']){$script:DpState.Workspace | Add-Member -NotePropertyName SecondaryIcons -NotePropertyValue @()}
    # Upgrade the old display scope once. Secondary coordinates are not pins.
    if(-not $script:DpState.Workspace.PSObject.Properties['DisplayPolicyVersion']){
        $script:DpState.Workspace.PrimaryOnly=$true
        $script:DpState.Workspace.SecondaryIcons=@()
        $script:DpState.Workspace | Add-Member -NotePropertyName DisplayPolicyVersion -NotePropertyValue 2
    }
    foreach($field in @('MonitorPagesEnabled','MonitorPages')){if(-not $script:DpState.Workspace.PSObject.Properties[$field]){$script:DpState.Workspace | Add-Member -NotePropertyName $field -NotePropertyValue $(if($field -eq 'MonitorPagesEnabled'){$false}else{@()})}}
    if(-not $script:DpState.PSObject.Properties['Revision']){$script:DpState | Add-Member -NotePropertyName Revision -NotePropertyValue ''}
    $script:DpWorkspaceRoot=Join-Path $script:DpStore 'Workspace'
    New-Item -ItemType Directory -Force $script:DpWorkspaceRoot,(Join-Path $script:DpWorkspaceRoot 'History'),(Join-Path $script:DpWorkspaceRoot 'HistoryIndex'),(Join-Path $script:DpWorkspaceRoot 'Profiles') | Out-Null
}
function Test-DpItemVisible {param($Entry,[int]$Page) ([int]$Entry.Page -eq $Page -or ($Entry.PSObject.Properties['Pinned'] -and [bool]$Entry.Pinned) -or (Test-DpSecondaryKey ([string]$Entry.Path)))}
function Get-DpActivePages {
    if(-not $script:DpState.PSObject.Properties['Workspace']){return @($script:DpState.Pages)}
    $all=@($script:DpState.Pages);$archived=@($script:DpState.Workspace.Archived)
    $order=@($script:DpState.Workspace.Order)+@($all | Where-Object {$_ -notin @($script:DpState.Workspace.Order)})
    @($order | Where-Object {$_ -in $all -and $_ -notin $archived} | Select-Object -Unique)
}
function Get-DpIncomingPage {
    if($script:DpState.PSObject.Properties['Workspace']){$page=[int]$script:DpState.Workspace.Inbox;if($page -gt 0 -and $page -in @(Get-DpActivePages)){return $page}}
    return [int]$script:DpState.Current
}
function Get-DpWorkspaceName {param([int]$Page)
    if($script:DpState.PSObject.Properties['Workspace']){
        $entry=@($script:DpState.Workspace.Names | Where-Object {[int]$_.Page -eq $Page})
        if($entry.Count){return [string]$entry[0].Name}
    }
    if('DeskPagesWorkspaceApi' -as [type]){return [DeskPagesWorkspaceApi]::FallbackName($Page)}
    return "Page $Page"
}
function Assert-DpWorkspaceState {param($State)
    if($null -eq $State -or $State.Desktop -ne $script:DpDesktop){throw 'Snapshot belongs to another Desktop.'}
    $pages=@($State.Pages);if(1 -notin $pages -or $pages.Count -gt 99 -or @($pages | Select-Object -Unique).Count -ne $pages.Count){throw 'Invalid snapshot pages.'}
    foreach($id in $pages){if([int]$id -lt 1 -or [int]$id -gt 99){throw 'Invalid page ID.'}}
    if([int]$State.Current -notin $pages){throw 'Snapshot has no active page.'}
    $systemIds=@('{20D04FE0-3AEA-1069-A2D8-08002B30309D}','{645FF040-5081-101B-9F08-00AA002F954E}','{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}')
    $seenSystems=@{};foreach($system in @($State.SystemIconBackup)){if($system.Id -notin $systemIds -or $seenSystems.ContainsKey([string]$system.Id)){throw 'Invalid system-icon snapshot.'};$seenSystems[[string]$system.Id]=$true}
    $seen=@{};foreach($entry in $State.Items){
        $path=[IO.Path]::GetFullPath([string]$entry.Path)
        if([IO.Path]::GetDirectoryName($path) -notin @($script:DpDesktop,$script:DpPublic) -or [int]$entry.Page -notin $pages -or $seen.ContainsKey($path)){throw 'Invalid or duplicate snapshot item.'};$seen[$path]=$true
        if($entry.PSObject.Properties['Pinned'] -and $entry.Pinned -isnot [bool]){throw 'Invalid shared-item flag.'}
    }
    $w=$State.Workspace
    if($null -eq $w -or [int]$w.Schema -ne 1 -or 1 -in @($w.Archived) -or [int]$State.Current -in @($w.Archived)){throw 'Invalid workspace snapshot.'}
    foreach($id in @($w.Archived)+@($w.Locked)+@($w.Order)){if([int]$id -notin $pages){throw 'Unknown workspace page.'}}
    if(@($w.Order).Count -ne $pages.Count -or @($w.Order | Select-Object -Unique).Count -ne $pages.Count){throw 'Invalid page order.'}
    if([int]$w.Inbox -ne 0 -and ([int]$w.Inbox -notin $pages -or [int]$w.Inbox -in @($w.Archived))){throw 'Inbox must be an active page.'}
    foreach($name in @($w.Names)){if([int]$name.Page -notin $pages){throw 'Unknown named page.'};$null=[DeskPagesPageNames]::Validate([string]$name.Name)}
    if($w.Language -notin @('en','ar','he') -or [int]$w.WheelThreshold -lt 10 -or [int]$w.WheelThreshold -gt 240 -or [int]$w.PointerThreshold -lt 100 -or [int]$w.PointerThreshold -gt 1500 -or [int]$w.EdgeWidth -lt 16 -or [int]$w.EdgeWidth -gt 120 -or [int]$w.EdgeDwell -lt 150 -or [int]$w.EdgeDwell -gt 1500){throw 'Invalid interaction settings.'}
    if($w.PSObject.Properties['MonitorPagesEnabled'] -and $w.MonitorPagesEnabled -isnot [bool]){throw 'Invalid monitor-page preference.'}
    $monitorIds=@{};$monitorPages=@{}
    foreach($assignment in @($w.MonitorPages | Where-Object {$null -ne $_})){
        if([string]::IsNullOrWhiteSpace([string]$assignment.Monitor) -or [int]$assignment.Page -notin $pages -or [int]$assignment.Page -in @($w.Archived) -or $monitorIds.ContainsKey([string]$assignment.Monitor) -or $monitorPages.ContainsKey([int]$assignment.Page)){throw 'Invalid or duplicate monitor page.'}
        $monitorIds[[string]$assignment.Monitor]=$true;$monitorPages[[int]$assignment.Page]=$true
    }
    if(@($monitorPages.Keys).Count -ge @($pages | Where-Object {$_ -notin @($w.Archived)}).Count){throw 'Keep at least one page for the main display.'}
    if($w.PSObject.Properties['PrimaryOnly'] -and $w.PrimaryOnly -isnot [bool]){throw 'Invalid display scope.'}
    if($w.PSObject.Properties['SecondaryIcons']){foreach($icon in @($w.SecondaryIcons | Where-Object {$null -ne $_})){if([string]::IsNullOrWhiteSpace([string]$icon.Key) -or [Math]::Abs([double]$icon.X) -gt 100000 -or [Math]::Abs([double]$icon.Y) -gt 100000){throw 'Invalid stationary screen icon.'}}}
    foreach($id in @($w.SystemPinned)){if($id -notin @('{20D04FE0-3AEA-1069-A2D8-08002B30309D}','{645FF040-5081-101B-9F08-00AA002F954E}','{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}')){throw 'Unknown pinned system icon.'}}
    if(@($w.Input).Count -ne 6){throw 'Invalid input preferences.'}
    foreach($value in $w.Input){if($value -isnot [bool]){throw 'Invalid input preference type.'}}
    if(@($w.Rules).Count -gt 100){throw 'Too many rules.'}
    foreach($rule in @($w.Rules)){
        if($rule.Kind -notin @('Extension','Name') -or [int]$rule.Page -notin $pages -or [string]::IsNullOrWhiteSpace($rule.Pattern) -or $rule.Pattern.Length -gt 80 -or [string]$rule.Pattern -match '[\x00-\x1f]'){throw 'Invalid organization rule.'}
        if($rule.Kind -eq 'Name'){$null=[Management.Automation.WildcardPattern]::new([string]$rule.Pattern,[Management.Automation.WildcardOptions]::IgnoreCase).IsMatch('validation')}
        elseif([string]$rule.Pattern -notmatch '^\.[^\\/:*?"<>|\s]{1,30}$'){throw 'Use an extension such as .png or .pdf.'}
    }
    foreach($id in @($w.Undo)+@($w.Redo)){if([string]$id -notmatch '^[a-f0-9]{32}$'){throw 'Invalid history record.'}}
}
function Get-DpWorkspaceSnapshot {
    $layouts=@(foreach($page in $script:DpState.Pages){$path=Join-Path $script:DpLayoutStore "page-$page.xml";if(Test-Path -LiteralPath $path){Import-Clixml -LiteralPath $path -ErrorAction Stop}})
    $identities=@(foreach($entry in $script:DpState.Items){$id=Get-DpWorkspaceIdentity $entry.Path;if($id){[PSCustomObject]@{Path=$entry.Path;Identity=$id}}})
    [PSCustomObject]@{State=Copy-DpData $script:DpState;Layouts=$layouts;Identities=$identities;At=[DateTime]::UtcNow}
}
function Get-DpWorkspaceIdentity {param([string]$Path)
    if(-not(Test-Path -LiteralPath $Path)){return ''}
    if($env:OS -eq 'Windows_NT'){return [DeskPagesLayout]::CreationIdentity($Path)}
    # Non-Windows test fixture only; production always uses the Windows file ID.
    return ([IO.File]::GetCreationTimeUtc($Path).Ticks.ToString())
}
function Assert-DpWorkspaceSnapshot {param($Snapshot)
    Assert-DpWorkspaceState $Snapshot.State
    $seen=@{};foreach($layout in @($Snapshot.Layouts)){
        $page=[int]$layout.Page;if($page -notin @($Snapshot.State.Pages) -or $seen.ContainsKey($page)){throw 'Invalid snapshot layout.'};$seen[$page]=$true
        if(@($layout.Icons).Count -gt 20000){throw 'Snapshot layout is too large.'}
        foreach($icon in @($layout.Icons)){if([Math]::Abs([double]$icon.X) -gt 100000 -or [Math]::Abs([double]$icon.Y) -gt 100000){throw 'Invalid snapshot coordinates.'}}
    }
}
function Set-DpSnapshotLayouts {param($Snapshot)
    Assert-DpWorkspaceSnapshot $Snapshot
    $keep=@($Snapshot.Layouts | ForEach-Object {'page-'+[int]$_.Page+'.xml'})
    foreach($file in Get-ChildItem -LiteralPath $script:DpLayoutStore -File -Filter 'page-*.xml'){
        if($file.Name -match '^page-[1-9][0-9]?\.xml$' -and $file.Name -notin $keep){[IO.File]::Delete($file.FullName)}
    }
    foreach($layout in @($Snapshot.Layouts)){Write-DpAtomicXml -Path (Join-Path $script:DpLayoutStore ("page-"+[int]$layout.Page+'.xml')) -Value $layout}
    $script:DpLastLayoutSignature=@{}
}
function Repair-DpWorkspaceTransaction {
    $path=Join-Path $script:DpWorkspaceRoot 'pending.xml';if(-not(Test-Path -LiteralPath $path)){return}
    $pending=Import-Clixml -LiteralPath $path -ErrorAction Stop
    Assert-DpWorkspaceSnapshot $pending.Before;Assert-DpWorkspaceSnapshot $pending.After
    if([string]$script:DpState.Revision -eq [string]$pending.After.State.Revision){Set-DpSnapshotLayouts $pending.After}
    else{Set-DpSnapshotLayouts $pending.Before}
    [IO.File]::Delete($path)
    Write-DpLog 'Recovered interrupted workspace metadata transaction; desktop visibility will follow the committed manifest.'
}
function Invoke-DpWorkspaceCommit {
    param([string]$Operation,$After,[bool]$Record=$true,$Before=$null)
    if($script:DpSuspended -or $script:DpEdgeDrag -or (Test-Path -LiteralPath (Join-Path $script:DpStore 'transition.xml'))){throw 'Finish the current interaction before changing the workspace.'}
    if($null -eq $Before){Save-DpVerifiedSourceLayout ([int]$script:DpState.Current);$Before=Get-DpWorkspaceSnapshot}
    Assert-DpWorkspaceSnapshot $Before
    $id=[guid]::NewGuid().ToString('N');$After.State.Revision=$id
    if($Record){$After.State.Workspace.Undo=@(@($Before.State.Workspace.Undo)+@($id) | Select-Object -Last 100);$After.State.Workspace.Redo=@()}
    $After.State.PageCount=@($After.State.Pages).Count
    Assert-DpWorkspaceSnapshot $After
    if($After.State.Workspace.MonitorPagesEnabled){
        $committed=$script:DpState
        try{$script:DpState=$After.State;$page=[int]$After.State.Current;$saved=@($After.Layouts | Where-Object {[int]$_.Page -eq $page} | ForEach-Object {$_.Icons});$null=@(Get-DpComposedLayoutIcons $page $saved @() $After.Layouts)}
        finally{$script:DpState=$committed}
    }
    $journal=Join-Path $script:DpWorkspaceRoot 'pending.xml'
    $pending=[PSCustomObject]@{Operation=$Operation;Before=$Before;After=$After;At=[DateTime]::UtcNow}
    Write-DpAtomicXml -Path $journal -Value $pending
    Invoke-DpFaultPoint 'WorkspaceJournal'
    try{
        if($Record){Write-DpHistoryRecord $id $pending}
        Set-DpSnapshotLayouts $After
        Invoke-DpFaultPoint 'WorkspaceLayouts'
        $script:DpState=$After.State;Save-DpState
        Invoke-DpFaultPoint 'WorkspaceManifest'
        Resume-DpCommittedPage
        Invoke-DpFaultPoint 'WorkspaceVisible'
        [IO.File]::Delete($journal)
        Publish-DpWorkspacePreferences
        Limit-DpWorkspaceHistory
        Write-DpLog ('Workspace operation committed: '+$Operation)
    }catch{
        $failure=$_
        try{$script:DpState=Read-DpManifest $script:DpManifest;Repair-DpWorkspaceTransaction;Resume-DpCommittedPage}
        catch{$script:DpSuspended=$true;Write-DpLog ('Workspace recovery pending; autosave suspended: '+[string]$_)}
        throw $failure
    }
}
function New-DpWorkspaceEdit {
    if($script:DpEdgeDrag){throw 'Finish dragging first.'}
    Save-DpVerifiedSourceLayout ([int]$script:DpState.Current)
    $before=Get-DpWorkspaceSnapshot
    [PSCustomObject]@{Before=$before;After=Copy-DpData $before}
}
function Restore-DpWorkspaceSnapshot {
    param($Snapshot,[string]$Operation='Restore workspace',[bool]$Record=$true,[object[]]$Undo=$null,[object[]]$Redo=$null)
    Assert-DpWorkspaceSnapshot $Snapshot
    $edit=New-DpWorkspaceEdit;$after=Copy-DpData $Snapshot
    # Files created since the checkpoint remain managed and visible on Main.
    $known=@{};foreach($item in $after.State.Items){$known[[string]$item.Path]=$true}
    foreach($item in $edit.Before.State.Items){if(-not $known.ContainsKey([string]$item.Path)){$copy=Copy-DpData $item;$copy.Page=1;$after.State.Items=@($after.State.Items)+@($copy)}}
    if($Snapshot.PSObject.Properties['Identities']){
        $ids=@{};foreach($identityRecord in $Snapshot.Identities){$ids[[string]$identityRecord.Path]=[string]$identityRecord.Identity}
        foreach($entry in $after.State.Items){
            if($ids.ContainsKey([string]$entry.Path)){$currentId=Get-DpWorkspaceIdentity $entry.Path;if($currentId -and $currentId -ne $ids[[string]$entry.Path]){$entry.Page=1;$entry | Add-Member -NotePropertyName Pinned -NotePropertyValue $false -Force}}
        }
    }
    foreach($field in @('PrimaryOnly','SecondaryIcons','DisplayPolicyVersion','MonitorPagesEnabled','MonitorPages')){if(-not $after.State.Workspace.PSObject.Properties[$field]){$after.State.Workspace | Add-Member -NotePropertyName $field -NotePropertyValue (Copy-DpData $script:DpState.Workspace.$field)}}
    if($null -ne $Undo){$after.State.Workspace.Undo=@($Undo)}
    if($null -ne $Redo){$after.State.Workspace.Redo=@($Redo)}
    foreach($field in @('Undo','Redo')){$after.State.Workspace.$field=@($after.State.Workspace.$field | Where-Object {Test-Path -LiteralPath (Join-Path $script:DpWorkspaceRoot ("History/"+$_+'.xml'))})}
    Invoke-DpWorkspaceCommit -Operation $Operation -After $after -Before $edit.Before -Record $Record
}
function Invoke-DpWorkspaceUndo {param([bool]$Redo=$false)
    $w=$script:DpState.Workspace;$ids=@($(if($Redo){$w.Redo}else{$w.Undo}))
    if(-not $ids.Count){throw 'There is nothing to undo or redo.'}
    $id=[string]$ids[-1];$record=Import-Clixml -LiteralPath (Join-Path $script:DpWorkspaceRoot "History/$id.xml") -ErrorAction Stop
    $remaining=@($ids | Select-Object -SkipLast 1)
    if($Redo){Restore-DpWorkspaceSnapshot -Snapshot $record.After -Operation 'Redo' -Record $false -Undo @(@($w.Undo)+@($id)) -Redo $remaining}
    else{Restore-DpWorkspaceSnapshot -Snapshot $record.Before -Operation 'Undo' -Record $false -Undo $remaining -Redo @(@($w.Redo)+@($id))}
}
function Get-DpSearchResults {param([string]$Query)
    $query=$Query.Trim();$names=@{};foreach($page in $script:DpState.Pages){$names[[int]$page]=Get-DpWorkspaceName $page}
    foreach($entry in $script:DpState.Items){
        $name=[IO.Path]::GetFileName([string]$entry.Path)
        if($query.Length -and $name.IndexOf($query,[StringComparison]::CurrentCultureIgnoreCase) -lt 0 -and ([string]$names[[int]$entry.Page]).IndexOf($query,[StringComparison]::CurrentCultureIgnoreCase) -lt 0){continue}
        [PSCustomObject]@{Path=$entry.Path;Name=$name;Page=[int]$entry.Page;PageName=$names[[int]$entry.Page];Pinned=[bool]($entry.PSObject.Properties['Pinned'] -and $entry.Pinned);Exists=Test-Path -LiteralPath $entry.Path;Archived=[int]$entry.Page -in @($script:DpState.Workspace.Archived)}
    }
}
function Set-DpWorkspaceItems {param([string[]]$Paths,[int]$Page,[ValidateSet('Move','Pin','Unpin')][string]$Mode='Move')
    if(-not $Paths.Count -or @($Paths | Select-Object -Unique).Count -ne $Paths.Count){throw 'Select one or more distinct managed files.'}
    if($Page -notin @(Get-DpActivePages)){throw 'Choose an active destination page.'}
    $edit=New-DpWorkspaceEdit;$entries=@($edit.After.State.Items | Where-Object {$_.Path -in $Paths})
    if($entries.Count -ne $Paths.Count){throw 'Selection contains unmanaged files.'}
    foreach($entry in $entries){
        if(-not(Test-Path -LiteralPath $entry.Path)){throw 'A selected file no longer exists.'}
        if(([int][IO.File]::GetAttributes($entry.Path) -band 1024) -ne 0){throw 'Cloud placeholders and links are not supported for page membership.'}
        if($Mode -eq 'Move'){$entry.Page=$Page}
        else{$entry | Add-Member -NotePropertyName Pinned -NotePropertyValue ($Mode -eq 'Pin') -Force}
    }
    Update-DpMembershipLayouts -Before $edit.Before -After $edit.After -Paths $Paths
    Invoke-DpWorkspaceCommit -Operation ($Mode+' '+$entries.Count+' items') -After $edit.After -Before $edit.Before
}
function Update-DpMembershipLayouts {param($Before,$After,[string[]]$Paths,[bool]$PreserveGroup=$true)
    $map=@{};foreach($layout in $Before.Layouts){foreach($icon in $layout.Icons){if($icon.Key -and -not $map.ContainsKey([string]$icon.Key)){$map[[string]$icon.Key]=$icon}}}
    $members=@{};foreach($entry in $After.State.Items){$members[[string]$entry.Path]=$entry}
    $sourcePages=@{};foreach($entry in $Before.State.Items){$sourcePages[[string]$entry.Path]=[int]$entry.Page}
    $screens=@(Get-DpPageScreens);$spacing=[DeskPagesLayout]::Spacing($script:originalDesktop)
    $After.Layouts=@(foreach($page in $After.State.Pages){
        $old=@($Before.Layouts | Where-Object {[int]$_.Page -eq $page})
        $icons=@(if($old.Count){foreach($icon in @($old[0].Icons)){
            if($members.ContainsKey([string]$icon.Key)){if(Test-DpItemVisible $members[[string]$icon.Key] $page){Copy-DpData $icon}}
            elseif($page -eq 1 -or @($After.State.Workspace.SystemPinned | Where-Object {([string]$icon.Key).IndexOf([string]$_,[StringComparison]::OrdinalIgnoreCase) -ge 0}).Count){Copy-DpData $icon}
        }})
        $keys=@{};foreach($icon in $icons){$keys[[string]$icon.Key]=$true}
        $new=@(foreach($path in $Paths){if($members.ContainsKey($path) -and (Test-DpItemVisible $members[$path] $page) -and -not $keys.ContainsKey($path) -and $map.ContainsKey($path)){Copy-DpData $map[$path]}})
        $groups=@($new | Group-Object -Property {if($PreserveGroup){[string]$sourcePages[[string]$_.Key]}else{[string]$_.Key}})
        foreach($group in $groups){
            $batch=@($group.Group)
            $source=[DeskPagesDragPlacement+Item[]]@(foreach($icon in $batch){[DeskPagesDragPlacement+Item]@{Key=$icon.Key;X=$icon.X;Y=$icon.Y}})
            $obstacles=[DeskPagesDragPlacement+Item[]]@(foreach($icon in $icons){[DeskPagesDragPlacement+Item]@{Key=$icon.Key;X=$icon.X;Y=$icon.Y}})
            $positions=$null
            foreach($screen in $screens){
                $g=[int[]]@($batch[0].X,$batch[0].Y,$screen.Left,$screen.Top,$screen.Right,$screen.Bottom)
                $positions=[DeskPagesDragPlacement]::Plan($g,$spacing.X,$spacing.Y,$false,0,0,$batch[0].X,$batch[0].Y,$source,$obstacles)
                if($null -ne $positions){break}
            }
            if($null -eq $positions){throw 'The destination has no free space for this group. No membership was changed.'}
            foreach($position in $positions){foreach($icon in $batch){if($icon.Key -eq $position.Key){$icon.X=$position.X;$icon.Y=$position.Y}}}
            $icons=@($icons)+@($batch)
        }
        [PSCustomObject]@{Page=[int]$page;SavedAt=[DateTime]::UtcNow;Icons=@($icons)}
    })
}
function Update-DpSystemPinLayouts {param($Before,$After)
    $templates=@{};foreach($layout in $Before.Layouts){foreach($icon in $layout.Icons){foreach($id in @($Before.State.SystemIconBackup | ForEach-Object {$_.Id})){if(([string]$icon.Key).IndexOf([string]$id,[StringComparison]::OrdinalIgnoreCase) -ge 0){$templates[[string]$id]=$icon}}}}
    $screens=@(Get-DpPageScreens);$spacing=[DeskPagesLayout]::Spacing($script:originalDesktop)
    $After.Layouts=@(foreach($page in $After.State.Pages){
        $prior=@($After.Layouts | Where-Object {[int]$_.Page -eq $page});$icons=@(if($prior.Count){foreach($icon in $prior[0].Icons){
            $system=@($After.State.SystemIconBackup | Where-Object {([string]$icon.Key).IndexOf([string]$_.Id,[StringComparison]::OrdinalIgnoreCase) -ge 0})
            if($page -eq 1 -or -not $system.Count -or $system[0].Id -in @($After.State.Workspace.SystemPinned)){Copy-DpData $icon}
        }})
        foreach($id in @($After.State.Workspace.SystemPinned)){
            if(-not $templates.ContainsKey([string]$id) -or @($icons | Where-Object {([string]$_.Key).IndexOf([string]$id,[StringComparison]::OrdinalIgnoreCase) -ge 0}).Count){continue}
            $icon=Copy-DpData $templates[[string]$id];$target=$null
            foreach($screen in $screens){$g=[int[]]@(($icon.X+[int]($spacing.X/2)),($icon.Y+16),$screen.Left,$screen.Top,$screen.Right,$screen.Bottom);$target=Resolve-DpNewItemPosition $g $spacing.X $spacing.Y $icons;if($null -ne $target){break}}
            if($null -eq $target){throw 'No free position for shared system icons. Settings were not changed.'}
            $icon.X=$target.X;$icon.Y=$target.Y;$icons=@($icons)+@($icon)
        }
        [PSCustomObject]@{Page=[int]$page;SavedAt=[DateTime]::UtcNow;Icons=@($icons)}
    })
}
function Save-DpDragUndo {
    if($null -eq $script:DpDragUndoBefore){Save-DpState;return}
    Save-DpAppliedWorkspaceOperation $script:DpDragUndoBefore 'Drag items across pages'
    $script:DpDragUndoBefore=$null
}
function Save-DpAppliedWorkspaceOperation {param($Before,[string]$Operation)
    $id=[guid]::NewGuid().ToString('N')
    $script:DpState.Workspace.Undo=@(@($script:DpState.Workspace.Undo)+@($id) | Select-Object -Last 100)
    $script:DpState.Workspace.Redo=@();$script:DpState.Revision=$id
    $after=Get-DpWorkspaceSnapshot
    Write-DpHistoryRecord $id ([PSCustomObject]@{Before=$Before;After=$after;Operation=$Operation;At=[DateTime]::UtcNow})
    Save-DpState
}
function Write-DpHistoryRecord {param([string]$Id,$Record)
    if($Id -notmatch '^[a-f0-9]{32}$'){throw 'Invalid history ID.'}
    Write-DpAtomicXml -Path (Join-Path $script:DpWorkspaceRoot "History/$Id.xml") -Value $Record
    Write-DpAtomicXml -Path (Join-Path $script:DpWorkspaceRoot "HistoryIndex/$Id.xml") -Value ([PSCustomObject]@{At=$Record.At;Operation=$Record.Operation;Count=@($Record.Before.State.Items).Count})
}
function Limit-DpWorkspaceHistory {
    # Keep the last 200 recovery records and every currently reachable undo/redo record.
    # Failed cleanup is diagnostic only and must never roll back a committed operation.
    try{
        $files=@(Get-ChildItem -LiteralPath (Join-Path $script:DpWorkspaceRoot 'HistoryIndex') -Filter '*.xml' | Where-Object {$_.BaseName -match '^[a-f0-9]{32}$'} | Sort-Object LastWriteTimeUtc -Descending)
        $keep=@($script:DpState.Workspace.Undo)+@($script:DpState.Workspace.Redo)+@($files | Select-Object -First 200 | ForEach-Object {$_.BaseName})
        foreach($file in $files){if($file.BaseName -notin $keep){foreach($relative in @('History','HistoryIndex')){foreach($suffix in @('.xml','.xml.previous')){$path=Join-Path $script:DpWorkspaceRoot ($relative+'/'+$file.BaseName+$suffix);if(Test-Path -LiteralPath $path){[IO.File]::Delete($path)}}}}}
    }catch{Write-DpLog 'History cleanup deferred; committed workspace remains intact.'}
}
function Get-DpRulePreview {
    $w=$script:DpState.Workspace
    foreach($entry in $script:DpState.Items){
        if(($entry.PSObject.Properties['Pinned'] -and $entry.Pinned) -or -not(Test-Path -LiteralPath $entry.Path)){continue}
        $name=[IO.Path]::GetFileName([string]$entry.Path)
        foreach($rule in $w.Rules){
            if([int]$rule.Page -notin @(Get-DpActivePages)){continue}
            $matches=if($rule.Kind -eq 'Extension'){[IO.Path]::GetExtension($name) -ieq $rule.Pattern}else{[Management.Automation.WildcardPattern]::new([string]$rule.Pattern,[Management.Automation.WildcardOptions]::IgnoreCase).IsMatch($name)}
            if($matches){if([int]$entry.Page -ne [int]$rule.Page){[PSCustomObject]@{Path=$entry.Path;From=[int]$entry.Page;To=[int]$rule.Page}};break}
        }
    }
}
function Invoke-DpRules {
    $preview=@(Get-DpRulePreview);if(-not $preview.Count){return}
    $edit=New-DpWorkspaceEdit
    foreach($move in $preview){foreach($entry in $edit.After.State.Items){if($entry.Path -eq $move.Path){$entry.Page=$move.To}}}
    Update-DpMembershipLayouts -Before $edit.Before -After $edit.After -Paths @($preview | ForEach-Object {$_.Path}) -PreserveGroup $false
    Invoke-DpWorkspaceCommit -Operation ('Apply '+$preview.Count+' reviewed suggestions') -After $edit.After -Before $edit.Before
}
function Publish-DpWorkspacePreferences {
    if(-not('DeskPagesWorkspaceApi' -as [type])){return}
    $w=$script:DpState.Workspace
    [DeskPagesWorkspaceApi]::Configure([string]$w.Language,[int]$w.WheelThreshold,[int]$w.PointerThreshold,[int]$w.EdgeWidth,[int]$w.EdgeDwell,[bool]$w.ReduceMotion)
    $names=@(foreach($page in $script:DpState.Pages){[string]$page+"`t"+(Get-DpWorkspaceName $page)})
    [DeskPagesWorkspaceApi]::SetNames([string[]]$names)
    [DeskPagesWorkspaceApi]::PrimaryOnly=[bool]$w.PrimaryOnly
    [DeskPagesWorkspaceApi]::Input=[bool[]]@($w.Input)
    [DeskPagesHost]::ApplyWorkspaceInput([bool[]]@($w.Input))
    [DeskPagesDragProtocol]::AtomicText((Join-Path $script:DpStore 'interaction.txt'),("DeskPagesInteraction1`n"+$w.EdgeWidth+"`n"+$w.EdgeDwell+"`n"+[bool]$w.PrimaryOnly))
}
function Get-DpCompatibilityReport {
    $rows=[Collections.Generic.List[string]]::new()
    $rows.Add('DeskPages compatibility report — '+[DateTime]::UtcNow.ToString('o'))
    $os=Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    $rows.Add('Windows: '+$os.DisplayVersion+'; build='+$os.CurrentBuildNumber+'.'+$os.UBR+'; 64-bit: '+[Environment]::Is64BitProcess)
    $rows.Add('Desktop: '+$script:DpDesktop);$rows.Add('Public Desktop: '+$script:DpPublic)
    $setting=Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' -Name Hidden -ErrorAction SilentlyContinue
    $rows.Add('Hidden files shown: '+($setting.Hidden -eq 1))
    $placeholders=@(Get-ChildItem -LiteralPath $script:DpDesktop,$script:DpPublic -Force | Where-Object {([int]$_.Attributes -band 1024) -ne 0})
    $rows.Add('Reparse/cloud items excluded from management: '+$placeholders.Count)
    $rows.Add('OneDrive Desktop: '+($script:DpDesktop -match '(?i)OneDrive'))
    foreach($screen in [Windows.Forms.Screen]::AllScreens){$rows.Add('Screen: '+$screen.DeviceName+' '+$screen.Bounds+' work='+$screen.WorkingArea)}
    try{$rows.Add([DeskPagesLayout]::DescribeView($script:originalDesktop))}catch{$rows.Add('Explorer unavailable: '+[string]$_)}
    $rows.Add('Touchpad driver support, display animation and reboot reliability require Windows device testing.')
    $rows.Add('OneDrive placeholders are unsupported. This report does not certify cloud synchronization.')
    return $rows.ToArray()
}

# Legacy secondary metadata must never turn displaced page items into shared pins.
function Test-DpSecondaryKey {param([string]$Key) return $false}
function Test-DpSecondarySystem {param([string]$Id) return $false}
function Get-DpSecondaryIcons {param([object[]]$Icons,[object[]]$Screens) return @()}
function Sync-DpSecondaryIcons {param([object[]]$Icons)}
function Get-DpPageScreens {
    $screens=@(Get-DpMonitorGeometry)
    if($script:DpState.Workspace.PrimaryOnly){$screens=@($screens | Where-Object Primary)}
    if(-not $screens.Count){throw 'Page display unavailable; saved layouts preserved.'}
    return $screens
}
function Get-DpScopedLayoutIcons {param([object[]]$Saved,[object[]]$Current,[int]$Page=$script:DpState.Current)
    if(@(Get-DpFixedMonitorPages).Count){return @(Get-DpComposedLayoutIcons $Page $Saved $Current)}
    $w=$script:DpState.Workspace
    if($null -eq $w -or -not $w.PSObject.Properties['PrimaryOnly'] -or -not $w.PrimaryOnly){return $Saved}
    $screens=@(Get-DpMonitorGeometry);$primary=@($screens | Where-Object Primary | Select-Object -First 1)
    if($primary.Count -ne 1){throw 'Primary screen geometry unavailable; saved layouts preserved.'}
    $p=$primary[0];$map=@{};foreach($icon in $Saved){$map[[string]$icon.Key]=Copy-DpData $icon}
    foreach($icon in $Current){if(-not $map.ContainsKey([string]$icon.Key)){$map[[string]$icon.Key]=Copy-DpData $icon}}
    # Keep valid primary positions and fit displaced icons onto Primary only.
    $spacing=[DeskPagesLayout]::Spacing($script:originalDesktop);$placed=[Collections.Generic.List[object]]::new();$outside=[Collections.Generic.List[object]]::new()
    foreach($icon in @($map.Values | Sort-Object Key)){
        # Existing free-form positions belong to the user, even when closer
        # than Explorer's grid spacing. Reserve them before fitting spillover.
        if($icon.X -ge $p.Left -and $icon.Y -ge $p.Top -and $icon.X -lt $p.Right -and $icon.Y -lt $p.Bottom){$placed.Add($icon)}else{$outside.Add($icon)}
    }
    foreach($icon in $outside){$g=@($p.Left,$p.Top,$p.Left,$p.Top,$p.Right,$p.Bottom);$target=Resolve-DpNewItemPosition -Geometry $g -SpacingX $spacing.X -SpacingY $spacing.Y -Occupied $placed.ToArray()
        if($null -eq $target){throw 'Primary screen has insufficient space. Saved layouts were preserved.'};$icon.X=$target.X;$icon.Y=$target.Y;$placed.Add($icon)
    }
    return @($map.Values)
}

# Layout files remain in main-display coordinates. Rendering translates each
# assigned page to its monitor; saving performs the inverse translation. This
# prevents Explorer's combined Desktop from contaminating page membership.
function Get-DpMonitorIdentity {param($Screen)
    if($Screen.PSObject.Properties['Identity'] -and $Screen.Identity){return [string]$Screen.Identity}
    return [string]$Screen.Device
}
function Get-DpFixedMonitorPages {param($State=$script:DpState,[object[]]$Screens=$null)
    if(-not $State.Workspace.MonitorPagesEnabled){return @()}
    if($null -eq $Screens -and $script:DpMonitorPageContext -and [object]::ReferenceEquals($State,$script:DpMonitorPageContext.State)){return $script:DpMonitorPageContext.Fixed}
    if($null -eq $Screens){$Screens=@(Get-DpMonitorGeometry)}
    $used=@{}
    foreach($screen in @($Screens | Where-Object {-not $_.Primary})){
        $id=Get-DpMonitorIdentity $screen
        $assignment=@($State.Workspace.MonitorPages | Where-Object {$_.Monitor -eq $id} | Select-Object -First 1)
        if($assignment.Count -and [int]$assignment[0].Page -in @($State.Pages) -and [int]$assignment[0].Page -notin @($State.Workspace.Archived) -and -not $used.ContainsKey([int]$assignment[0].Page)){
            $page=[int]$assignment[0].Page;$used[$page]=$true
            [PSCustomObject]@{Page=$page;Screen=$screen;Monitor=$id}
        }
    }
}
function Get-DpNavigationPages {
    $fixed=@(Get-DpFixedMonitorPages | ForEach-Object {$_.Page})
    @(Get-DpActivePages | Where-Object {$_ -notin $fixed})
}
function Get-DpVisiblePages {param([int]$Page,[object[]]$Screens=$null)
    @($Page)+@(Get-DpFixedMonitorPages -Screens $Screens | ForEach-Object {$_.Page}) | Select-Object -Unique
}
function Set-DpMonitorCurrent {param($State,[object[]]$Screens)
    $fixed=@(Get-DpFixedMonitorPages $State $Screens | ForEach-Object {$_.Page})
    if([int]$State.Current -in $fixed){
        $available=@($State.Workspace.Order | Where-Object {$_ -notin $fixed -and $_ -notin @($State.Workspace.Archived)})
        if(-not $available.Count){throw 'Keep one page available on the main display.'}
        $State.Current=[int]$available[0]
    }
}
function Get-DpIconOwnerPage {param($Icon,[int]$Page)
    $entry=@();if($script:DpMonitorPageContext){if($script:DpMonitorPageContext.Items.ContainsKey([string]$Icon.Key)){$entry=@($script:DpMonitorPageContext.Items[[string]$Icon.Key])}}else{$entry=@($script:DpState.Items | Where-Object {$_.Path -eq $Icon.Key} | Select-Object -First 1)}
    if($entry.Count){if($entry[0].PSObject.Properties['Pinned'] -and $entry[0].Pinned){return $Page};return [int]$entry[0].Page}
    foreach($system in @($script:DpState.SystemIconBackup)){
        $aliases=@([string]$system.Id);if($system.Id -eq '{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}'){$aliases+='{26EE0668-A00A-44D7-9371-BEB064C98683}'}
        foreach($id in $aliases){if(([string]$Icon.Key).IndexOf($id,[StringComparison]::OrdinalIgnoreCase) -ge 0){if($system.Id -in @($script:DpState.Workspace.SystemPinned)){return $Page};return 1}}
    }
    return $Page
}
function Get-DpIconScreen {param($Icon,[int]$Page)
    $owner=Get-DpIconOwnerPage $Icon $Page
    $fixed=@(Get-DpFixedMonitorPages | Where-Object {$_.Page -eq $owner})
    if($fixed.Count){return $fixed[0].Screen}
    if($script:DpMonitorPageContext){return $script:DpMonitorPageContext.Primary}
    return @(Get-DpMonitorGeometry | Where-Object Primary)[0]
}
function Test-DpPointInScreen {param($Icon,$Screen)
    return $null -ne $Screen -and $Icon.X -ge $Screen.Left -and $Icon.Y -ge $Screen.Top -and $Icon.X -lt $Screen.Right -and $Icon.Y -lt $Screen.Bottom
}
function Get-DpComposedLayoutIcons {param([int]$Page,[object[]]$Saved,[object[]]$Current,[object[]]$Layouts=$null)
    $previous=$script:DpMonitorPageContext
    $script:DpMonitorPageContext=New-DpMonitorPageContext
    try{
    $primary=@(Get-DpMonitorGeometry | Where-Object Primary)[0]
    if($null -eq $primary){throw 'Primary display unavailable; saved layouts preserved.'}
    $spacing=[DeskPagesLayout]::Spacing($script:originalDesktop)
    foreach($visible in @(Get-DpVisiblePages $Page)){
        $fixed=@(Get-DpFixedMonitorPages | Where-Object {$_.Page -eq $visible});$screen=$primary
        if($fixed.Count){$screen=$fixed[0].Screen}
        $icons=@();if($visible -eq $Page){$icons=@($Saved)}else{if($null -ne $Layouts){$icons=@($Layouts | Where-Object {[int]$_.Page -eq $visible} | ForEach-Object {$_.Icons})}else{$path=Join-Path $script:DpLayoutStore "page-$visible.xml";if(Test-Path -LiteralPath $path){$icons=@((Import-Clixml -LiteralPath $path).Icons)}}}
        $map=@{};foreach($icon in $icons){if((Get-DpIconOwnerPage $icon $Page) -eq $visible){$copy=Copy-DpData $icon;$copy.X=[int]($screen.Left+$copy.X-$primary.Left);$copy.Y=[int]($screen.Top+$copy.Y-$primary.Top);$map[[string]$copy.Key]=$copy}}
        # New icons have no canonical position yet; retain them only on their
        # owning display and fit the rest into available space before any write.
        foreach($icon in $Current){if((Get-DpIconOwnerPage $icon $Page) -eq $visible -and -not $map.ContainsKey([string]$icon.Key)){$map[[string]$icon.Key]=Copy-DpData $icon}}
        $placed=[Collections.Generic.List[object]]::new();$outside=[Collections.Generic.List[object]]::new()
        foreach($icon in @($map.Values | Sort-Object Key)){if(Test-DpPointInScreen $icon $screen){$placed.Add($icon)}else{$outside.Add($icon)}}
        foreach($icon in $outside){$g=@($screen.Left,$screen.Top,$screen.Left,$screen.Top,$screen.Right,$screen.Bottom);$target=Resolve-DpNewItemPosition $g $spacing.X $spacing.Y $placed.ToArray();if($null -eq $target){throw 'Assigned display has insufficient space. Saved page layouts were preserved.'};$icon.X=$target.X;$icon.Y=$target.Y;$placed.Add($icon)}
        $placed.ToArray()
    }
    }finally{$script:DpMonitorPageContext=$previous}
}
function Save-DpMonitorPageLayouts {param([int]$Page,[object[]]$Icons)
    $previous=$script:DpMonitorPageContext
    $script:DpMonitorPageContext=New-DpMonitorPageContext
    try{
    $primary=@(Get-DpMonitorGeometry | Where-Object Primary)[0]
    $snapshots=@(foreach($visible in @(Get-DpVisiblePages $Page)){
        $path=Join-Path $script:DpLayoutStore "page-$visible.xml"
        if($visible -in @($script:DpState.Workspace.Locked) -and (Test-Path -LiteralPath $path)){continue}
        $local=@(foreach($icon in $Icons){if((Get-DpIconOwnerPage $icon $Page) -eq $visible){$screen=Get-DpIconScreen $icon $Page;$copy=Copy-DpData $icon;$copy.X=[int]($primary.Left+$copy.X-$screen.Left);$copy.Y=[int]($primary.Top+$copy.Y-$screen.Top);$copy}})
        # Shared icons still need their main-display coordinates on every page.
        if($visible -ne $Page -and (Test-Path -LiteralPath $path)){$local+=@((Import-Clixml -LiteralPath $path).Icons | Where-Object {(Get-DpIconOwnerPage $_ $Page) -eq $Page})}

        [PSCustomObject]@{Page=$visible;SavedAt=[DateTime]::UtcNow;Icons=$local}
    })
    $changed=@(foreach($snapshot in $snapshots){$path=Join-Path $script:DpLayoutStore ("page-"+$snapshot.Page+'.xml');$old=@();if(Test-Path -LiteralPath $path){$old=@((Import-Clixml -LiteralPath $path).Icons)};$a=($snapshot.Icons | Sort-Object Key | ForEach-Object {"$($_.Key)|$($_.X)|$($_.Y)"}) -join "`n";$b=($old | Sort-Object Key | ForEach-Object {"$($_.Key)|$($_.X)|$($_.Y)"}) -join "`n";if($a -cne $b){$snapshot}})
    if($changed.Count){
        $journal=Join-Path $script:DpWorkspaceRoot 'monitor-layout-checkpoint.xml'
        Write-DpAtomicXml $journal ([PSCustomObject]@{Revision=[string]$script:DpState.Revision;Layouts=$changed})
        foreach($snapshot in $changed){Write-DpAtomicXml (Join-Path $script:DpLayoutStore ("page-"+$snapshot.Page+'.xml')) $snapshot}
        [IO.File]::Delete($journal)
    }
    if(-not $script:DpLastLayoutSignature){$script:DpLastLayoutSignature=@{}}
    $script:DpLastLayoutSignature[$Page]=($Icons | Sort-Object -Property @('Key','Name') | ForEach-Object {"$($_.Key)|$($_.Name)|$($_.X)|$($_.Y)"}) -join "`n"
    }finally{$script:DpMonitorPageContext=$previous}
}
function Get-DpGroupsEnabled {
    $path=Join-Path $env:LOCALAPPDATA 'DeskPages/TaskbarGroups/enabled.txt'
    return -not(Test-Path -LiteralPath $path) -or [IO.File]::ReadAllText($path).Trim() -ne 'off'
}
function Set-DpGroupsEnabled {param([bool]$Enabled)
    $path=Join-Path $env:LOCALAPPDATA 'DeskPages/TaskbarGroups/enabled.txt'
    $null=New-Item -ItemType Directory -Force -Path (Split-Path $path)
    if($Enabled){
        # A rapid off/on must wait for the old controller to restore ownership.
        $mutex=$null;$acquired=$false
        try{$mutex=[Threading.Mutex]::OpenExisting('Local\DeskPages.TaskbarGroups.'+[Security.Principal.WindowsIdentity]::GetCurrent().User.Value);try{$acquired=$mutex.WaitOne(3000)}catch [Threading.AbandonedMutexException]{$acquired=$true};if(-not $acquired){throw 'Taskbar groups are still restoring apps. Try enabling again.'}}
        catch [Threading.WaitHandleCannotBeOpenedException]{}
        finally{if($mutex){if($acquired){$mutex.ReleaseMutex()};$mutex.Dispose()}}
    }
    [DeskPagesDragProtocol]::AtomicText($path,$(if($Enabled){'on'}else{'off'}))
    if($Enabled){$script:originalDesktop.Document.Application.ShellExecute((Join-Path $script:base 'DeskPagesGroups.exe'),'',$script:base,'open',0)}
    else{$signal=$null;try{$signal=[Threading.EventWaitHandle]::OpenExisting('Local\DeskPagesTaskbarGroupsStop-'+[Security.Principal.WindowsIdentity]::GetCurrent().User.Value);$null=$signal.Set()}catch [Threading.WaitHandleCannotBeOpenedException]{}finally{if($signal){$signal.Dispose()}}}
}

function Repair-DpMonitorLayoutCheckpoint {
    $path=Join-Path $script:DpWorkspaceRoot 'monitor-layout-checkpoint.xml'
    if(-not(Test-Path -LiteralPath $path)){return}
    $pending=Import-Clixml -LiteralPath $path
    if([string]$pending.Revision -eq [string]$script:DpState.Revision){
        $probe=Get-DpWorkspaceSnapshot;$probe.Layouts=@($pending.Layouts);Assert-DpWorkspaceSnapshot $probe
        foreach($layout in $pending.Layouts){Write-DpAtomicXml (Join-Path $script:DpLayoutStore ("page-"+$layout.Page+'.xml')) $layout}
    }
    [IO.File]::Delete($path)
}

function New-DpMonitorPageContext {
    $screens=@(Get-DpMonitorGeometry);$items=@{}
    foreach($entry in $script:DpState.Items){$items[[string]$entry.Path]=$entry}
    [PSCustomObject]@{State=$script:DpState;Items=$items;Fixed=@(Get-DpFixedMonitorPages -Screens $screens);Primary=@($screens | Where-Object Primary)[0]}
}
