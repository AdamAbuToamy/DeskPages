function Sync-DpSnapshotScene {
    if(-not [DeskPagesHost]::SnapshotCacheEnabled){return}
    $parts=@($script:DpState.Items | Sort-Object Path | ForEach-Object {"$($_.Path)|$($_.Page)|$($_.Pinned)|$($_.Secondary)"})
    if($script:DpState.PSObject.Properties['Workspace']){$parts+=@($script:DpState.Workspace.SystemPinned | Sort-Object);$parts+=@($script:DpState.Workspace.SecondaryIcons | Sort-Object Key | ForEach-Object {"$($_.Key)|$($_.X)|$($_.Y)"})}
    $parts+=@($script:DpState.SystemIconBackup | Sort-Object Id | ForEach-Object {"$($_.Id)|$($_.Exists)|$($_.Value)"})
    $context=$parts -join "`n"
    if($script:DpSnapshotSceneContext -cne $context){$script:DpSnapshotKeyCache=@{};$script:DpSnapshotSceneContext=$context}
    [DeskPagesHost]::SnapshotSceneContext($context)
}
function Get-DpSnapshotKey {
    param([int]$Page,[object[]]$Icons)
    $cacheStamp=''
    # Rehash only when a canonical checkpoint or its membership/scope changes.
    # In particular, do not scan every file for every icon before showing a slide.
    if($null -eq $Icons -and $script:DpState.Workspace.PrimaryOnly -and $script:DpLayoutStore){
        $path=Join-Path $script:DpLayoutStore "page-$Page.xml"
        if(Test-Path -LiteralPath $path){
            $file=Get-Item -LiteralPath $path
            $primary=@($script:DpScopeScreens | Where-Object Primary | ForEach-Object {"$($_.Left),$($_.Top),$($_.Right),$($_.Bottom)"}) -join ';'
            $cacheStamp=$path+'|'+$file.LastWriteTimeUtc.Ticks+'|'+$file.Length+'|'+$primary+'|'+[bool]$script:DpState.Workspace.MonitorPagesEnabled
            if($script:DpSnapshotKeyCache -and $script:DpSnapshotKeyCache.ContainsKey($Page)){
                $cached=$script:DpSnapshotKeyCache[$Page]
                if($cached.Stamp -ceq $cacheStamp -and [object]::ReferenceEquals($cached.State,$script:DpState)){return $cached.Key}
            }
        }
    }
    # Source checkpoints are canonical per-page records, not the composed
    # Explorer view (which also contains stationary secondary-page icons).
    if($null -eq $Icons -and $script:DpState.Workspace.PrimaryOnly -and $script:DpLayoutStore){
        $path=Join-Path $script:DpLayoutStore "page-$Page.xml"
        if(Test-Path -LiteralPath $path){$Icons=@((Read-DpCachedLayout $Page).Icons)}
    }
    if($null -ne $Icons -and $script:DpState.Workspace.PrimaryOnly){
        if($script:DpState.Workspace.MonitorPagesEnabled){
            $entries=@{};foreach($entry in $script:DpState.Items){$entries[[string]$entry.Path]=$entry}
            $Icons=@($Icons | Where-Object {$entry=$entries[[string]$_.Key];if($entry){$entry.Pinned -or [int]$entry.Page -eq $Page}else{(Get-DpIconOwnerPage $_ $Page) -eq $Page}})
        }
        $primary=@($script:DpScopeScreens | Where-Object Primary)
        if($primary.Count -eq 1){$Icons=@($Icons | Where-Object {Test-DpPointInScreen $_ $primary[0]})}
    }
    if($null -ne $Icons){$signature=($Icons | Sort-Object -Property @('Key','Name') | ForEach-Object {"$($_.Key)|$($_.Name)|$($_.X)|$($_.Y)"}) -join "`n"}
    elseif($script:DpLastLayoutSignature -and $script:DpLastLayoutSignature.ContainsKey($Page)){$signature=$script:DpLastLayoutSignature[$Page]}
    else{return ''}
    $hash=[Security.Cryptography.SHA256]::Create()
    try{
        $key=[string]$Page+':'+([BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($signature)))).Replace('-','')
        if($cacheStamp){if(-not $script:DpSnapshotKeyCache){$script:DpSnapshotKeyCache=@{}};$script:DpSnapshotKeyCache[$Page]=[PSCustomObject]@{Stamp=$cacheStamp;State=$script:DpState;Key=$key}}
        return $key
    }
    finally{$hash.Dispose()}
}
function Start-DpNavigationSample {if($script:hostReady){[DeskPagesHost]::BeginNavigationSample()}}
function Remember-DpSnapshotKey {param([int]$Page,[string]$Key)
    if(-not $script:DpSnapshotKeys){$script:DpSnapshotKeys=@{}}
    if($Key){$script:DpSnapshotKeys[$Page]=$Key}
}
function Try-DpCachedPreview {param([int]$Source,[int]$Target,[int]$Direction)
    if(-not [DeskPagesHost]::SnapshotCacheEnabled){return $false}
    Sync-DpSnapshotScene
    if(-not $script:DpSnapshotKeys){$script:DpSnapshotKeys=@{}}
    # The native session owns scope validity. Never retain an obsolete key
    # across a real scene/primary-geometry change in this script dictionary.
    foreach($page in @($Source,$Target)){
        $known=[DeskPagesHost]::SavedSnapshotKey($page)
        # A fixed page may have been edited while absent from main navigation.
        # Compare its canonical checkpoint before reusing its former main image.
        if($known -and $script:DpLayoutStore -and (Test-Path -LiteralPath (Join-Path $script:DpLayoutStore "page-$page.xml"))){$expected=Get-DpSnapshotKey -Page $page;if($expected -and $known -cne $expected){$known=''}}
        if($known){$script:DpSnapshotKeys[$page]=$known}else{$script:DpSnapshotKeys.Remove($page)}
    }
    if(-not $script:DpSnapshotKeys.ContainsKey($Source) -or -not $script:DpSnapshotKeys.ContainsKey($Target)){return $false}
    return [DeskPagesHost]::TryBeginCachedPreview($Direction,$script:DpSnapshotKeys[$Source],$script:DpSnapshotKeys[$Target])
}
function Set-DpSnapshotSource {param([int]$Page)
    if([DeskPagesHost]::SnapshotCacheEnabled){Sync-DpSnapshotScene;$key=Get-DpSnapshotKey -Page $Page;Remember-DpSnapshotKey $Page $key;[DeskPagesHost]::SetSnapshotSource($key)}
}
function Set-DpSnapshotTarget {param([int]$Page,[object[]]$Icons)
    if([DeskPagesHost]::SnapshotCacheEnabled){Sync-DpSnapshotScene;$key=Get-DpSnapshotKey -Page $Page -Icons $Icons;Remember-DpSnapshotKey $Page $key;[DeskPagesHost]::SetSnapshotTarget($key)}
}
function Warm-DpCurrentSnapshot {
    if($script:DpSuspended -or -not [DeskPagesHost]::SnapshotCacheEnabled -or (Test-Path -LiteralPath (Join-Path $script:DpStore 'transition.xml'))){return}
    Sync-DpSnapshotScene
    $page=[int]$script:DpState.Current
    Save-DpVerifiedSourceLayout $page
    $key=Get-DpSnapshotKey -Page $page;Remember-DpSnapshotKey $page $key;if([DeskPagesHost]::NeedsSnapshotCapture($key)){[DeskPagesHost]::WarmSnapshot($key)}
}
