function Test-DpFileVisible {param($Entry,[int]$Page)
    if((Get-Command Get-DpVisiblePages -ErrorAction SilentlyContinue) -and [int]$Entry.Page -in @(Get-DpVisiblePages $Page $script:DpScopeScreens)){return $true}
    if(Get-Command Test-DpItemVisible -ErrorAction SilentlyContinue){return (Test-DpItemVisible $Entry $Page)}
    return ([int]$Entry.Page -eq $Page -or ($Entry.PSObject.Properties['Pinned'] -and [bool]$Entry.Pinned))
}
# Shared production/test transaction engine. Adapters live in Visibility/Layouts.
function Write-DpAtomicXml {
    param([Parameter(Mandatory)][string]$Path,[Parameter(Mandatory)]$Value)
    $temporary=$Path+'.'+[Guid]::NewGuid().ToString('N')+'.tmp'
    try {
        $Value | Export-Clixml -LiteralPath $temporary -Depth 100
        $stream=[IO.File]::Open($temporary,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
        try {$stream.Flush($true)} finally {$stream.Dispose()}
        if ([IO.File]::Exists($Path)) {[IO.File]::Replace($temporary,$Path,($Path+'.previous'))}
        else {[IO.File]::Move($temporary,$Path)}
    } finally {if([IO.File]::Exists($temporary)){[IO.File]::Delete($temporary)}}
}
function Invoke-DpFaultPoint {param([string]$Stage)}
function Save-DpState {Write-DpAtomicXml -Path $script:DpManifest -Value $script:DpState}
function Read-DpManifest {
    param([string]$Path)
    # A previous manifest can contain older membership. Never silently promote it.
    try {Import-Clixml -LiteralPath $Path -ErrorAction Stop}
    catch {throw 'The page manifest is unreadable. Startup stopped without changing visibility. Keep all backups and run Rescue.cmd.'}
}
function Save-DpVerifiedSourceLayout {
    param([int]$Page)
    if((Get-Command Sync-DpMonitorProfile -ErrorAction SilentlyContinue) -and -not $script:DpMonitorSyncBusy){Sync-DpMonitorProfile}
    try{Save-DpPageLayout $Page}
    catch{
        if(-not $_.ToString().Contains('layout saving is blocked.')){throw}
        # Never save a mixed Explorer view. Repair the committed page first.
        Write-DpLog 'Source view is stale; restoring committed membership/layout before checkpoint.'
        Resume-DpCommittedPage
        Save-DpPageLayout $Page
    }
}
function Complete-DpPagePresentation {
    if(-not $script:hostReady){return}
    # The durable page is already committed. Optional rendering must never
    # trigger a second Explorer refresh or a layout rollback.
    try{[DeskPagesHost]::FinishAnimation()}
    catch{try{[DeskPagesHost]::AbortAnimation()}catch{};Write-DpLog ('Page committed; animation skipped: '+$_)}
    if(Get-Command Start-DpNavigationSample -ErrorAction SilentlyContinue){try{[DeskPagesHost]::EndNavigationSample($true)}catch{Write-DpLog ('Navigation sample skipped: '+$_)}}
}
function Invoke-DpPageTransaction {
    param([int]$Page,[bool]$Animate=$true,[ValidateRange(-1,1)][int]$Direction=0)
    if ($script:DpSuspended) {throw 'DeskPages is suspended. Resume explicitly before switching.'}
    $source=[int]$script:DpState.Current
    if ($source -eq $Page) {return}
    if((Get-Command Get-DpFixedMonitorPages -ErrorAction SilentlyContinue) -and $Page -in @((Get-DpFixedMonitorPages) | ForEach-Object {$_.Page})){throw 'This page is displayed on another monitor. Change its assignment in settings first.'}
    if ($Page -notin @($script:DpState.Pages)) {throw 'Unknown page.'}
    if($script:DpState.PSObject.Properties['Workspace'] -and $Page -in @($script:DpState.Workspace.Archived)){throw 'Restore this archived page before opening it.'}
    if($script:hostReady -and (Get-Command Start-DpNavigationSample -ErrorAction SilentlyContinue)){Start-DpNavigationSample}
    $motion=[Math]::Sign($Page-$source)
    if($Direction -ne 0){$motion=$Direction}
    elseif(Get-Command Get-DpActivePages -ErrorAction SilentlyContinue){$order=[int[]]@(Get-DpActivePages);$motion=[Math]::Sign([Array]::IndexOf($order,$Page)-[Array]::IndexOf($order,$source))}
    $earlyPreview=$false
    try {
        if($Animate -and $script:hostReady -and (Get-Command Try-DpCachedPreview -ErrorAction SilentlyContinue)){$earlyPreview=Try-DpCachedPreview -Source $source -Target $Page -Direction $motion}
        Save-DpVerifiedSourceLayout $source
        # Discover new files while the source page is still intact.
        Set-DpFilePage -Page $source -Commit:$false
        $journal=Join-Path $script:DpStore 'transition.xml'
        Write-DpAtomicXml -Path $journal -Value ([PSCustomObject]@{From=$source;To=$Page;At=[DateTime]::UtcNow})
        Invoke-DpFaultPoint 'JournalWritten'
        if ($Animate -and $script:hostReady -and -not $earlyPreview) {if(Get-Command Set-DpSnapshotSource -ErrorAction SilentlyContinue){Set-DpSnapshotSource $source};[DeskPagesHost]::BeginAnimation($motion)}
        Set-DpFilePage -Page $Page -Commit:$false -RegisterNew:$false
        Invoke-DpFaultPoint 'VisibilityApplied'
        $systemChanged=Set-DpSystemIcons $Page
        Invoke-DpFaultPoint 'SystemApplied'
        # Pages 2+ share hidden system-icon state. Their file membership still
        # changes and Explorer notifications alone can leave the prior view.
        # The opaque source cover stays presented throughout refresh/restore.
        Refresh-DpDesktop
        Restore-DpPageLayout $Page
        Invoke-DpFaultPoint 'LayoutApplied'
        if ($Animate -and $script:hostReady -and [DeskPagesAnimation]::IsVisible) {
            $targetIcons=@(Confirm-DpAnimationView -Page $Page)
            if(Get-Command Set-DpSnapshotTarget -ErrorAction SilentlyContinue){Set-DpSnapshotTarget -Page $Page -Icons $targetIcons}
            [DeskPagesHost]::ConfirmAnimationTarget($targetIcons.Count)
        }
        # Commit only after visibility, system icons AND verified layout succeed.
        $script:DpState.Current=$Page
        Save-DpState
        Invoke-DpFaultPoint 'ManifestCommitted'
        [IO.File]::Delete($journal)
        Invoke-DpFaultPoint 'JournalRemoved'
        Complete-DpPagePresentation
    }
    catch {
        $failure=$_
        if ($script:hostReady) {[DeskPagesHost]::AbortAnimation();if(Get-Command Start-DpNavigationSample -ErrorAction SilentlyContinue){[DeskPagesHost]::EndNavigationSample($false)}}
        # If commit already reached disk, restore that committed page, not an old guess.
        try {
            $disk=Read-DpManifest $script:DpManifest
            $script:DpState=$disk
            Resume-DpCommittedPage
        }
        catch {
            $script:DpSuspended=$true
            Write-DpLog "Rollback incomplete; autosave blocked: $_"
        }
        throw $failure
    }
}
function Resume-DpCommittedPage {
    # A process killed at any transition stage restarts from manifest.Current.
    # Do not register files or save a layout from the partially painted shell.
    $page=[int]$script:DpState.Current
    Set-DpFilePage -Page $page -Commit:$false -RegisterNew:$false
    Set-DpSystemIcons $page
    Refresh-DpDesktop
    Restore-DpPageLayout $page
    $journal=Join-Path $script:DpStore 'transition.xml'
    if ([IO.File]::Exists($journal)) {[IO.File]::Delete($journal)}
}
function Suspend-DpSession {
    $script:DpSuspended=$true
    Write-DpAtomicXml -Path (Join-Path $script:DpStore 'suspended.xml') -Value ([PSCustomObject]@{At=[DateTime]::UtcNow;Current=$script:DpState.Current;Reason='Show all / recovery'})
}
function Restore-DpStartupPage {
    param([bool]$Main=$false)
    $previous=[int]$script:DpState.Current
    try{
        if(-not $Main -or $previous -eq 1){Resume-DpCommittedPage;return}
        $selection=Join-Path $script:DpStore 'startup-selection.xml'
        Write-DpAtomicXml -Path $selection -Value ([PSCustomObject]@{From=$previous;To=1;At=[DateTime]::UtcNow})
        # Keep manifest.Current unchanged until main-page visibility/layout are verified.
        Set-DpFilePage -Page 1 -Commit:$false -RegisterNew:$false
        $null=Set-DpSystemIcons 1
        Refresh-DpDesktop
        Restore-DpPageLayout 1
        $script:DpState.Current=1
        Save-DpState
        foreach($name in @('transition.xml','startup-selection.xml')){
            $path=Join-Path $script:DpStore $name
            if(Test-Path -LiteralPath $path){[IO.File]::Delete($path)}
        }
    }catch{if(Test-Path -LiteralPath $script:DpManifest){$script:DpState=Read-DpManifest $script:DpManifest}else{$script:DpState.Current=$previous};throw}
}
