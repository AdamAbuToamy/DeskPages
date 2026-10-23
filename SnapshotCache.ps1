function Sync-DpSnapshotScene {
    if(-not [DeskPagesHost]::SnapshotCacheEnabled){return}
    $parts=@($script:DpState.Items | Sort-Object Path | ForEach-Object {"$($_.Path)|$($_.Page)|$($_.Pinned)|$($_.Secondary)"})
    if($script:DpState.PSObject.Properties['Workspace']){$parts+=@($script:DpState.Workspace.SystemPinned | Sort-Object);$parts+=@($script:DpState.Workspace.SecondaryIcons | Sort-Object Key | ForEach-Object {"$($_.Key)|$($_.X)|$($_.Y)"})}
    if($script:DpState.Workspace.MonitorPagesEnabled){$parts+=@('MonitorPages', [string]$script:DpState.Workspace.MonitorKey);$shared=@{};foreach($entry in $script:DpState.Items){if($entry.Pinned){$shared[[string]$entry.Path]=$true}};foreach($fixed in @(Get-DpFixedMonitorPages -Screens $script:DpScopeScreens)){$path=Join-Path $script:DpLayoutStore ("page-"+$fixed.Page+'.xml');if(Test-Path -LiteralPath $path){$parts+=@((Read-DpCachedLayout ([int]$fixed.Page)).Icons | Where-Object {$icon=$_;-not $shared.ContainsKey([string]$icon.Key) -and -not @($script:DpState.Workspace.SystemPinned | Where-Object {([string]$icon.Key).IndexOf([string]$_,[StringComparison]::OrdinalIgnoreCase) -ge 0}).Count} | Sort-Object Key | ForEach-Object {"$($_.Key)|$($_.X)|$($_.Y)"})}}}
    $parts+=@($script:DpState.SystemIconBackup | Sort-Object Id | ForEach-Object {"$($_.Id)|$($_.Exists)|$($_.Value)"})
    [DeskPagesHost]::SnapshotSceneContext(($parts -join "`n"))
}
function Get-DpSnapshotKey {
    param([int]$Page,[object[]]$Icons)
    if($null -ne $Icons){$signature=($Icons | Sort-Object -Property @('Key','Name') | ForEach-Object {"$($_.Key)|$($_.Name)|$($_.X)|$($_.Y)"}) -join "`n"}
    elseif($script:DpLastLayoutSignature -and $script:DpLastLayoutSignature.ContainsKey($Page)){$signature=$script:DpLastLayoutSignature[$Page]}
    else{return ''}
    $hash=[Security.Cryptography.SHA256]::Create()
    try{return [string]$Page+':'+([BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($signature)))).Replace('-','')}
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
    foreach($page in @($Source,$Target)){if(-not $script:DpSnapshotKeys.ContainsKey($page)){$known=[DeskPagesHost]::SavedSnapshotKey($page);if($known){$script:DpSnapshotKeys[$page]=$known}}}
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
