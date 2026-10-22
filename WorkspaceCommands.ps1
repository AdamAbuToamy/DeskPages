function Get-DpMonitorGeometry {
    @(foreach($screen in [Windows.Forms.Screen]::AllScreens){
        $g=[DeskPagesLayout]::ContextGeometry($screen.WorkingArea.Left+1,$screen.WorkingArea.Top+1)
        [PSCustomObject]@{Device=$screen.DeviceName;Left=$g[2];Top=$g[3];Right=$g[4];Bottom=$g[5];Primary=$screen.Primary}
    })
}
function Get-DpMonitorKey {
    $spacing=[DeskPagesLayout]::Spacing($script:originalDesktop)
    $text=(@(Get-DpMonitorGeometry | Sort-Object Device | ForEach-Object {"$($_.Device)|$($_.Left)|$($_.Top)|$($_.Right)|$($_.Bottom)|$($_.Primary)"}) -join ';')+"|$($spacing.X)|$($spacing.Y)|primary-v2=$($script:DpState.Workspace.PrimaryOnly)"
    $sha=[Security.Cryptography.SHA256]::Create();try{([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($text)))).Replace('-','').Substring(0,24)}finally{$sha.Dispose()}
}
function Convert-DpMonitorLayouts {param([object[]]$Layouts,[object[]]$OldScreens,[object[]]$NewScreens,[int]$SpacingX,[int]$SpacingY)
    if($NewScreens.Count -lt 1 -or $SpacingX -lt 16 -or $SpacingY -lt 16){throw 'Invalid monitor geometry.'}
    if($script:DpState.Workspace.PrimaryOnly){$NewScreens=@($NewScreens | Where-Object Primary);if($NewScreens.Count -ne 1){throw 'Primary display unavailable; saved layouts preserved.'}}
    foreach($layout in $Layouts){
        $copy=Copy-DpData $layout;$placed=[Collections.Generic.List[object]]::new()
        foreach($icon in @($copy.Icons)){
            $old=@($OldScreens | Where-Object {$icon.X -ge $_.Left -and $icon.X -lt $_.Right -and $icon.Y -ge $_.Top -and $icon.Y -lt $_.Bottom} | Select-Object -First 1)
            $screen=@($NewScreens | Where-Object {$old.Count -and $_.Device -eq $old[0].Device} | Select-Object -First 1)
            if(-not $screen.Count){$screen=@($NewScreens | Where-Object Primary | Select-Object -First 1)}
            if(-not $screen.Count){$screen=@($NewScreens[0])};$s=$screen[0]
            $x=[int]$icon.X;$y=[int]$icon.Y
            if($old.Count){$o=$old[0];$x=[int]($s.Left+($icon.X-$o.Left)*($s.Right-$s.Left)/[Math]::Max(1,$o.Right-$o.Left));$y=[int]($s.Top+($icon.Y-$o.Top)*($s.Bottom-$s.Top)/[Math]::Max(1,$o.Bottom-$o.Top))}
            $g=@(($x+[int]($SpacingX/2)),($y+16),$s.Left,$s.Top,$s.Right,$s.Bottom)
            $target=Resolve-DpNewItemPosition -Geometry $g -SpacingX $SpacingX -SpacingY $SpacingY -Occupied $placed.ToArray()
            if($null -eq $target){
                foreach($alternative in $NewScreens){$g=@($alternative.Left,$alternative.Top,$alternative.Left,$alternative.Top,$alternative.Right,$alternative.Bottom);$target=Resolve-DpNewItemPosition -Geometry $g -SpacingX $SpacingX -SpacingY $SpacingY -Occupied $placed.ToArray();if($null -ne $target){break}}
            }
            if($null -eq $target){throw 'The attached displays have insufficient space. Previous monitor layouts were preserved.'}
            $icon.X=$target.X;$icon.Y=$target.Y;$placed.Add($target)
        }
        $copy
    }
}
function Sync-DpMonitorProfile {
    if($script:DpSuspended -or $script:DpEdgeDrag -or $script:DpMonitorSyncBusy){return}
    if(Test-Path -LiteralPath (Join-Path $script:DpStore 'transition.xml')){return}
    $script:DpScopeScreens=@(Get-DpMonitorGeometry)
    $key=Get-DpMonitorKey;$w=$script:DpState.Workspace
    if($key -eq $w.MonitorKey){return}
    if($script:DpNewItemContext -and (Get-Command Stop-DpNewItemPlacement -ErrorAction SilentlyContinue)){Stop-DpNewItemPlacement}
    $before=Get-DpWorkspaceSnapshot;$screens=@(Get-DpMonitorGeometry)
    $profilePath=Join-Path $script:DpWorkspaceRoot "Profiles/$key.xml"
    $oldScreens=@()
    if($w.MonitorKey){
        $oldPath=Join-Path $script:DpWorkspaceRoot ("Profiles/"+$w.MonitorKey+'.xml')
        if(Test-Path -LiteralPath $oldPath){$oldScreens=@((Import-Clixml -LiteralPath $oldPath).Screens)}
        Write-DpAtomicXml $oldPath ([PSCustomObject]@{Screens=$oldScreens;Layouts=$before.Layouts})
    }
    $after=Copy-DpData $before
    $profile=$null;if(Test-Path -LiteralPath $profilePath){$profile=Import-Clixml -LiteralPath $profilePath}
    $spacing=[DeskPagesLayout]::Spacing($script:originalDesktop)
    $after.Layouts=@(foreach($layout in $before.Layouts){
        $saved=@($profile.Layouts | Where-Object {[int]$_.Page -eq [int]$layout.Page})
        # Revalidate saved profiles too: older releases allowed secondary spillover.
        if($saved.Count){Convert-DpMonitorLayouts @($saved[0]) $screens $screens $spacing.X $spacing.Y}else{Convert-DpMonitorLayouts @($layout) $oldScreens $screens $spacing.X $spacing.Y}
    })
    $after.State.Workspace.MonitorKey=$key
    $script:DpMonitorSyncBusy=$true
    try{Invoke-DpWorkspaceCommit -Operation 'Display configuration changed' -After $after -Before $before -Record $false}
    finally{$script:DpMonitorSyncBusy=$false}
    Write-DpAtomicXml $profilePath ([PSCustomObject]@{Screens=$screens;Layouts=$after.Layouts})
}
function Invoke-DpWorkspaceRequest {
    param([string]$Command,[string[]]$Arguments)
    if($script:DpWorkspaceBusy){throw 'Another workspace operation is in progress.'}
    $script:DpWorkspaceBusy=$true
    try{
        if($script:DpSuspended){throw 'Workspace is suspended. Use recovery before making changes.'}
        if($script:DpEdgeDrag -or [DeskPagesAnimation]::IsVisible){throw 'Finish the current drag or page animation first.'}
        switch($Command){
            'pages' {
                # The workspace lists tracked membership, not a disk-health scan.
                # Never probe cloud/network-backed Desktop paths while opening UI.
                $counts=@{}
                foreach($item in $script:DpState.Items){$id=[int]$item.Page;if(-not $counts.ContainsKey($id)){$counts[$id]=0};$counts[$id]++}
                foreach($page in $script:DpState.Workspace.Order){$count=0;if($counts.ContainsKey([int]$page)){$count=$counts[[int]$page]};[string]$page+"`t"+(Get-DpWorkspaceName $page)+"`t"+$count+"`t"+($page -in @($script:DpState.Workspace.Archived))+"`t"+($page -in @($script:DpState.Workspace.Locked))+"`t"+($page -eq [int]$script:DpState.Current)}
            }
            'preview' {
                $page=[int]$Arguments[0];$path=Join-Path $script:DpLayoutStore "page-$page.xml"
                if(Test-Path -LiteralPath $path){foreach($icon in (Import-Clixml -LiteralPath $path).Icons){[string]$icon.X+"`t"+$icon.Y+"`t"+$icon.Name}}
            }
            'search' {foreach($row in Get-DpSearchResults $Arguments[0]){[string]$row.Page+"`t"+$row.PageName+"`t"+$row.Exists+"`t"+$row.Pinned+"`t"+$row.Archived+"`t"+$row.Path}}
            'reveal' {
                $path=$Arguments[0];$entries=@($script:DpState.Items | Where-Object {$_.Path -eq $path});if($entries.Count -ne 1 -or -not(Test-Path -LiteralPath $path)){throw 'File was removed or renamed. Refresh the search.'}
                $page=[int]$entries[0].Page;if($page -in @($script:DpState.Workspace.Archived)){throw 'Restore this archived page first.'}
                if($page -ne [int]$script:DpState.Current){Set-DeskPage $page -Animate $false}
                Connect-DpDesktop
                $item=$script:originalDesktop.Document.Folder.ParseName([IO.Path]::GetFileName($path))
                if($null -eq $item){throw 'Explorer cannot select this item yet.'}
                $script:originalDesktop.Document.SelectItem($item,29)
            }
            'open' {Set-DeskPage ([int]$Arguments[0])}
            'add' {
                $edit=New-DpWorkspaceEdit;$ids=@($edit.After.State.Pages);$id=@(1..99 | Where-Object {$_ -notin $ids}) | Select-Object -First 1
                if(-not $id){throw 'Maximum of 99 pages reached.'}
                $edit.After.State.Pages=@($ids)+@([int]$id);$edit.After.State.Workspace.Order=@($edit.After.State.Workspace.Order)+@([int]$id)
                $edit.After.State.Workspace.Names=@($edit.After.State.Workspace.Names)+@([PSCustomObject]@{Page=[int]$id;Name=[DeskPagesPageNames]::Validate($Arguments[0])})
                $edit.After.State.Current=[int]$id
                Invoke-DpWorkspaceCommit 'Add page' $edit.After -Before $edit.Before
            }
            'reorder' {
                $source=[int]$Arguments[0];$target=[int]$Arguments[1]
                if($source -notin @($script:DpState.Pages) -or $target -notin @($script:DpState.Pages)){throw 'Unknown page.'}
                if($source -ne $target){$edit=New-DpWorkspaceEdit;$list=[Collections.Generic.List[int]]::new();foreach($id in $edit.After.State.Workspace.Order){$list.Add([int]$id)};$null=$list.Remove($source);$list.Insert($list.IndexOf($target),$source);$edit.After.State.Workspace.Order=@($list.ToArray());Invoke-DpWorkspaceCommit 'Reorder page cards' $edit.After -Before $edit.Before}
            }
            'rename' {
                $page=[int]$Arguments[0];if($page -notin @($script:DpState.Pages)){throw 'Unknown page.'}
                $edit=New-DpWorkspaceEdit;$edit.After.State.Workspace.Names=@($edit.After.State.Workspace.Names | Where-Object {[int]$_.Page -ne $page})+@([PSCustomObject]@{Page=$page;Name=[DeskPagesPageNames]::Validate($Arguments[1])})
                Invoke-DpWorkspaceCommit 'Rename page' $edit.After -Before $edit.Before
            }
            {$_ -in @('archive','unarchive','lock','unlock','delete','up','down')} {
                $page=[int]$Arguments[0];if($page -notin @($script:DpState.Pages)){throw 'Unknown page.'}
                $edit=New-DpWorkspaceEdit;$w=$edit.After.State.Workspace
                if($Command -in @('archive','delete') -and $page -eq 1){throw 'The main recovery page must remain available.'}
                if($Command -eq 'archive'){$w.Archived=@($w.Archived | Where-Object {$_ -ne $page})+@($page);if([int]$w.Inbox -eq $page){$w.Inbox=0};if([int]$edit.After.State.Current -eq $page){$edit.After.State.Current=1}}
                elseif($Command -eq 'unarchive'){$w.Archived=@($w.Archived | Where-Object {$_ -ne $page})}
                elseif($Command -eq 'lock'){$w.Locked=@($w.Locked | Where-Object {$_ -ne $page})+@($page)}
                elseif($Command -eq 'unlock'){$w.Locked=@($w.Locked | Where-Object {$_ -ne $page})}
                elseif($Command -eq 'delete'){
                    if(@(Get-DpPageDeleteBlockers @($edit.After.State.Items) $page).Count){throw 'This page contains files. Archive it or move its files first.'}
                    $edit.After.State.Pages=@($edit.After.State.Pages | Where-Object {$_ -ne $page});$edit.After.State.Items=@($edit.After.State.Items | Where-Object {[int]$_.Page -ne $page})
                    foreach($field in @('Order','Archived','Locked')){$w.$field=@($w.$field | Where-Object {$_ -ne $page})}
                    $w.Names=@($w.Names | Where-Object {[int]$_.Page -ne $page});$w.Rules=@($w.Rules | Where-Object {[int]$_.Page -ne $page});if([int]$w.Inbox -eq $page){$w.Inbox=0}
                    if([int]$edit.After.State.Current -eq $page){$edit.After.State.Current=1};$edit.After.Layouts=@($edit.After.Layouts | Where-Object {[int]$_.Page -ne $page})
                }else{
                    $order=[int[]]@($w.Order);$i=[Array]::IndexOf($order,$page);$j=$i+$(if($Command -eq 'up'){-1}else{1})
                    if($i -ge 0 -and $j -ge 0 -and $j -lt $order.Length){$order[$i]=$order[$j];$order[$j]=$page;$w.Order=@($order)}
                }
                Invoke-DpWorkspaceCommit $Command $edit.After -Before $edit.Before
            }
            {$_ -in @('move','pin','unpin')} {Set-DpWorkspaceItems -Paths @($Arguments | Select-Object -Skip 1) -Page ([int]$Arguments[0]) -Mode $Command}
            'undo' {Invoke-DpWorkspaceUndo}
            'redo' {Invoke-DpWorkspaceUndo -Redo $true}
            'history' {
                foreach($file in Get-ChildItem -LiteralPath (Join-Path $script:DpWorkspaceRoot 'HistoryIndex') -Filter '*.xml' | Where-Object {$_.BaseName -match '^[a-f0-9]{32}$'} | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 100){
                    $record=Import-Clixml -LiteralPath $file.FullName;[string]$file.BaseName+"`t"+$record.At.ToString('u')+"`t"+$record.Operation+"`t"+$record.Count
                }
            }
            'checkpoint' {
                $edit=New-DpWorkspaceEdit;Invoke-DpWorkspaceCommit 'Manual checkpoint' $edit.After -Before $edit.Before
            }
            'restore-preview' {
                if($Arguments[0] -notmatch '^[a-f0-9]{32}$'){throw 'Invalid recovery point.'}
                $record=Import-Clixml -LiteralPath (Join-Path $script:DpWorkspaceRoot ('History/'+$Arguments[0]+'.xml'));Assert-DpWorkspaceSnapshot $record.Before
                'Operation: '+$record.Operation;'Saved: '+$record.At.ToString('u');'Pages: '+@($record.Before.State.Pages).Count;'Managed items: '+@($record.Before.State.Items).Count;'Restores organization only. Newer files stay managed on Main. File contents are not restored.'
                $old=@{};foreach($entry in $script:DpState.Items){$old[[string]$entry.Path]=[int]$entry.Page}
                foreach($entry in $record.Before.State.Items){if($old.ContainsKey([string]$entry.Path) -and $old[[string]$entry.Path] -ne [int]$entry.Page){[string]$old[[string]$entry.Path]+' -> '+$entry.Page+' : '+$entry.Path}}
            }
            'restore' {
                if($Arguments[0] -notmatch '^[a-f0-9]{32}$'){throw 'Invalid recovery point.'}
                $record=Import-Clixml -LiteralPath (Join-Path $script:DpWorkspaceRoot ('History/'+$Arguments[0]+'.xml'));Restore-DpWorkspaceSnapshot $record.Before
            }
            'rule-add' {
                $edit=New-DpWorkspaceEdit;$edit.After.State.Workspace.Rules=@($edit.After.State.Workspace.Rules)+@([PSCustomObject]@{Kind=$Arguments[0];Pattern=$Arguments[1].Trim();Page=[int]$Arguments[2]})
                Invoke-DpWorkspaceCommit 'Add organization rule' $edit.After -Before $edit.Before
            }
            'rules' {foreach($rule in $script:DpState.Workspace.Rules){[string]$rule.Page+"`t"+$rule.Kind+"`t"+$rule.Pattern}}
            'rule-remove' {$edit=New-DpWorkspaceEdit;$i=[int]$Arguments[0];$n=-1;$edit.After.State.Workspace.Rules=@($edit.After.State.Workspace.Rules | Where-Object {$n++;$n -ne $i});Invoke-DpWorkspaceCommit 'Remove organization rule' $edit.After -Before $edit.Before}
            'rules-preview' {foreach($move in Get-DpRulePreview){[string]$move.From+"`t"+$move.To+"`t"+$move.Path}}
            'rules-apply' {
                $now=@(Get-DpRulePreview | ForEach-Object {[string]$_.From+"`t"+$_.To+"`t"+$_.Path})
                if(($now -join "`n") -cne ($Arguments -join "`n")){throw 'Suggestions changed. Review them again before applying.'}
                Invoke-DpRules
            }
            'settings' {
                $w=$script:DpState.Workspace
                @([string]$w.Inbox,[string]$w.Language,[string]$w.ReduceMotion,[string]$w.WheelThreshold,[string]$w.PointerThreshold,[string]$w.EdgeWidth,[string]$w.EdgeDwell)+@($w.Input | ForEach-Object {[string]$_})+@('{20D04FE0-3AEA-1069-A2D8-08002B30309D}','{645FF040-5081-101B-9F08-00AA002F954E}','{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}' | ForEach-Object {[string]($_ -in @($w.SystemPinned))})
            }
            'capture-settings' {[string][bool][DeskPagesHost]::SnapshotCacheEnabled}
            'capture-mode' {if($Arguments.Count -ne 1){throw 'Invalid capture mode.'};[DeskPagesHost]::SetSnapshotMode([bool]::Parse($Arguments[0]))}
            'display-settings' {[string][bool]$script:DpState.Workspace.PrimaryOnly}
            'display-scope' {
                if($Arguments.Count -ne 1){throw 'Invalid display scope.'}
                $enabled=[bool]::Parse($Arguments[0]);if($enabled -eq [bool]$script:DpState.Workspace.PrimaryOnly){break}
                $edit=New-DpWorkspaceEdit;$w=$edit.After.State.Workspace
                $w.PrimaryOnly=$enabled
                $w.SecondaryIcons=@()
                if($enabled){$screens=@(Get-DpMonitorGeometry);$spacing=[DeskPagesLayout]::Spacing($script:originalDesktop);$prior=$script:DpState.Workspace.PrimaryOnly;try{$script:DpState.Workspace.PrimaryOnly=$true;$edit.After.Layouts=@(Convert-DpMonitorLayouts $edit.After.Layouts $screens $screens $spacing.X $spacing.Y)}finally{$script:DpState.Workspace.PrimaryOnly=$prior}}
                $w.MonitorKey=''
                Invoke-DpWorkspaceCommit -Operation 'Change display scope' -After $edit.After -Before $edit.Before
            }
            'preferences-save' {
                if($Arguments.Count -ne 9 -or $Arguments[0] -notin @('en','ar','he')){throw 'Invalid preferences.'}
                $values=@(foreach($argument in $Arguments[1..7]){[bool]::Parse($argument)})
                $after=Copy-DpData $script:DpState;$w=$after.Workspace
                $w.Language=$Arguments[0];$w.Input[0]=$values[0];$w.Input[5]=$values[1];$w.Input[3]=$values[2];$w.Input[4]=$values[3];$w.Input[1]=$values[4];$w.Input[2]=$values[5];$w.ReduceMotion=$values[6];$w.Inbox=[int]$Arguments[8]
                Assert-DpWorkspaceState $after
                # Preferences do not touch membership, saved layouts or Explorer.
                Write-DpAtomicXml -Path $script:DpManifest -Value $after
                $script:DpState=$after
                Publish-DpWorkspacePreferences
            }
            'save-layout' {
                Save-DpVerifiedSourceLayout ([int]$script:DpState.Current)
                $snapshot=Get-DpWorkspaceSnapshot;Assert-DpWorkspaceSnapshot $snapshot
                Write-DpAtomicXml -Path (Join-Path $script:DpWorkspaceRoot 'saved-layout.xml') -Value ([PSCustomObject]@{Format='DeskPagesSavedLayout1';SavedAt=[DateTime]::UtcNow;Snapshot=$snapshot})
            }
            {$_ -in @('saved-layout-info','restore-layout')} {
                $path=Join-Path $script:DpWorkspaceRoot 'saved-layout.xml'
                if(-not(Test-Path -LiteralPath $path)){throw 'Save a layout first.'}
                $saved=Import-Clixml -LiteralPath $path
                if($saved.Format -ne 'DeskPagesSavedLayout1'){throw 'Invalid saved layout.'}
                Assert-DpWorkspaceSnapshot $saved.Snapshot
                if($Command -eq 'restore-layout'){
                    $snapshot=Copy-DpData $saved.Snapshot
                    # Restoring organization must not revert the app's current preferences.
                    foreach($field in @('PrimaryOnly','SecondaryIcons','DisplayPolicyVersion','Language','Input','ReduceMotion','WheelThreshold','PointerThreshold','EdgeWidth','EdgeDwell')){$snapshot.State.Workspace | Add-Member -NotePropertyName $field -NotePropertyValue (Copy-DpData $script:DpState.Workspace.$field) -Force}
                    $inbox=[int]$script:DpState.Workspace.Inbox
                    $snapshot.State.Workspace.Inbox=$(if($inbox -in @($snapshot.State.Pages) -and $inbox -notin @($snapshot.State.Workspace.Archived)){$inbox}else{0})
                    Restore-DpWorkspaceSnapshot -Snapshot $snapshot -Operation 'Restore saved layout'
                }
                else{$saved.SavedAt.ToLocalTime().ToString('g')}
            }
            'pinned-icons' {
                foreach($entry in $script:DpState.Items){if(Test-Path -LiteralPath $entry.Path){"file`t$([bool]($entry.PSObject.Properties['Pinned'] -and $entry.Pinned))`t$([int]$entry.Page)`t$($entry.Path)"}}
                foreach($id in @('{20D04FE0-3AEA-1069-A2D8-08002B30309D}','{645FF040-5081-101B-9F08-00AA002F954E}','{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}')){"system`t$($id -in @($script:DpState.Workspace.SystemPinned))`t$id`tWindows"}
            }
            'pins-save' {
                $edit=New-DpWorkspaceEdit;$paths=@();$seen=@{}
                $allowed=@('{20D04FE0-3AEA-1069-A2D8-08002B30309D}','{645FF040-5081-101B-9F08-00AA002F954E}','{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}')
                foreach($row in $Arguments){
                    $f=$row.Split([char]9);if($f.Count -ne 3 -or $seen.ContainsKey($f[2])){throw 'Invalid pinned icon request.'};$seen[$f[2]]=$true;$value=[bool]::Parse($f[1])
                    if($f[0] -eq 'file'){
                        $entries=@($edit.After.State.Items | Where-Object {$_.Path -eq $f[2]})
                        if($entries.Count -ne 1 -or -not(Test-Path -LiteralPath $f[2]) -or ([int][IO.File]::GetAttributes($f[2]) -band 1024)){throw 'Select an existing managed desktop file.'}
                        $entries[0] | Add-Member -NotePropertyName Pinned -NotePropertyValue $value -Force;$paths+=@($f[2])
                    }elseif($f[0] -eq 'system' -and $f[2] -in $allowed){
                        $edit.After.State.Workspace.SystemPinned=@($edit.After.State.Workspace.SystemPinned | Where-Object {$_ -ne $f[2]});if($value){$edit.After.State.Workspace.SystemPinned+=@($f[2])}
                    }else{throw 'Unknown pinned icon.'}
                }
                if($paths.Count){Update-DpMembershipLayouts -Before $edit.Before -After $edit.After -Paths $paths}
                Update-DpSystemPinLayouts $edit.Before $edit.After
                if($Arguments.Count){Invoke-DpWorkspaceCommit -Operation 'Update pinned icons' -Before $edit.Before -After $edit.After}
            }
            {$_ -in @('pin-selected','unpin-selected')} {
                Connect-DpDesktop
                $paths=@($script:originalDesktop.Document.SelectedItems() | ForEach-Object {[string]$_.Path})
                if(-not $paths.Count){throw 'Select desktop files first.'}
                Set-DpWorkspaceItems -Paths $paths -Page ([int]$script:DpState.Current) -Mode $(if($Command -eq 'pin-selected'){'Pin'}else{'Unpin'})
            }
            'settings-save' {
                if($Arguments.Count -notin @(7,13,16)){throw 'Invalid settings.'}
                $edit=New-DpWorkspaceEdit;$w=$edit.After.State.Workspace
                $w.Inbox=[int]$Arguments[0];$w.Language=$Arguments[1];$w.ReduceMotion=[bool]::Parse($Arguments[2]);$w.WheelThreshold=[int]$Arguments[3];$w.PointerThreshold=[int]$Arguments[4];$w.EdgeWidth=[int]$Arguments[5];$w.EdgeDwell=[int]$Arguments[6]
                if($Arguments.Count -ge 13){$w.Input=@($Arguments[7..12] | ForEach-Object {[bool]::Parse($_)})}
                if($Arguments.Count -eq 16){$ids=@('{20D04FE0-3AEA-1069-A2D8-08002B30309D}','{645FF040-5081-101B-9F08-00AA002F954E}','{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}');$w.SystemPinned=@(for($i=0;$i -lt 3;$i++){if([bool]::Parse($Arguments[13+$i])){$ids[$i]}})}
                Update-DpSystemPinLayouts $edit.Before $edit.After
                Invoke-DpWorkspaceCommit 'Change workspace settings' $edit.After -Before $edit.Before
            }
            'document' {
                if($Arguments[0] -notin @('PRIVACY.txt','LICENSE.txt','START-HERE-AR.md')){throw 'Unknown document.'}
                Start-Process notepad.exe -ArgumentList ('"'+(Join-Path $script:base $Arguments[0])+'"')
            }
            'compatibility' {Get-DpCompatibilityReport}
            'input-settings' {[DeskPagesHost]::InputSettings()}
            'input-test' {[DeskPagesHost]::Diagnostics()}
            'rescue' {Start-Process -FilePath (Join-Path $script:base 'DeskPagesRescue.exe')}
            default {throw 'Unknown workspace request.'}
        }
    }finally{
        $script:DpWorkspaceBusy=$false
        if($script:hostReady){[DeskPagesHost]::UpdatePages([int[]]@(Get-DpActivePages),[int]$script:DpState.Current)}
    }
}
