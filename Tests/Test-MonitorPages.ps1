$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
. (Join-Path $base 'StateEngine.ps1');. (Join-Path $base 'Workspace.ps1');. (Join-Path $base 'WorkspaceCommands.ps1')
function Assert($Value,$Why){if(-not $Value){throw $Why}}
function Get-DpMonitorGeometry {throw 'Retired assignments must not query monitor geometry.'}
function Get-DpWorkspaceIdentity {param($Path) return 'fixture'}
function Write-DpLog {param($Message)}
$temp=Join-Path ([IO.Path]::GetTempPath()) ('dp-retire-monitor-pages-'+[guid]::NewGuid().ToString('N'))
try{
 $script:DpStore=$temp;$script:DpDesktop=Join-Path $temp 'Desktop';$script:DpPublic=Join-Path $temp 'Public';$script:DpLayoutStore=Join-Path $temp 'Layouts';$script:DpManifest=Join-Path $temp 'pages.xml'
 New-Item -ItemType Directory -Force $temp,$script:DpDesktop,$script:DpPublic,$script:DpLayoutStore | Out-Null
 $pin=Join-Path $script:DpDesktop 'pin.txt';[IO.File]::WriteAllText($pin,'unchanged file bytes')
 $script:DpState=[PSCustomObject]@{Desktop=$script:DpDesktop;Current=2;Pages=@(1,2,3,4);Items=@([PSCustomObject]@{Path=$pin;Page=2;Pinned=$true});SystemIconBackup=@()}
 Initialize-DpWorkspace
 foreach($page in 1..4){Write-DpAtomicXml (Join-Path $script:DpLayoutStore "page-$page.xml") ([PSCustomObject]@{Page=$page;Icons=@([PSCustomObject]@{Key=$pin;Name='pin';X=(17+$page);Y=12})})}
 $before=@{};foreach($file in Get-ChildItem $script:DpLayoutStore){$before[$file.Name]=(Get-FileHash $file.FullName).Hash}
 $w=$script:DpState.Workspace;$w.PrimaryOnly=$false;$w.MonitorPagesEnabled=$true;$w.MonitorPages=@([PSCustomObject]@{Monitor='panel-two';Page=2},[PSCustomObject]@{Monitor='panel-three';Page=3});$w.MonitorKey='previous-primary-origin-profile'
 Initialize-DpWorkspace
 Assert ($w.PrimaryOnly -and -not $w.MonitorPagesEnabled -and $w.MonitorPages.Count -eq 0) 'Legacy settings were not retired.'
 Assert ($w.MonitorKey -eq 'previous-primary-origin-profile') 'Primary-origin profile was lost before migration.'
 Assert ($script:DpState.Current -eq 2 -and $script:DpState.Items[0].Pinned) 'Migration changed current page or permanent pin membership.'
 Assert ((@(Get-DpNavigationPages) -join ',') -eq '1,2,3,4') 'A formerly assigned page is missing from navigation.'
 Assert ((@(Get-DpVisiblePages 2) -join ',') -eq '2') 'Secondary page remains visible.'
 foreach($file in Get-ChildItem $script:DpLayoutStore){Assert ((Get-FileHash $file.FullName).Hash -eq $before[$file.Name]) 'Retiring assignments rewrote canonical layouts.'}
 $archive=Join-Path $script:DpWorkspaceRoot 'retired-monitor-pages.xml';$hash=(Get-FileHash $archive).Hash
 Assert (@((Import-Clixml $archive).MonitorPages).Count -eq 2) 'Old assignment metadata was not backed up.'
 Initialize-DpWorkspace;Assert ((Get-FileHash $archive).Hash -eq $hash) 'Migration overwrote the retired settings backup.'
 $failed=$false;try{Invoke-DpWorkspaceRequest 'monitor-pages-save' @('True')}catch{$failed=$true};Assert $failed 'Retired assignment API re-enabled the feature.'
 $w.PrimaryOnly=$false;$w.MonitorPagesEnabled=$true;$w.MonitorPages=@([PSCustomObject]@{Monitor='stale-import';Page=3})
 Set-DpPrimaryDisplayPolicy $script:DpState
 Assert ($w.PrimaryOnly -and -not $w.MonitorPagesEnabled -and $w.MonitorPages.Count -eq 0) 'Old import/history settings escaped policy normalization.'
 Assert ([IO.File]::ReadAllText($pin) -eq 'unchanged file bytes') 'File content changed.'
 Write-Host 'PASS: legacy two/three-monitor assignments retired automatically; all pages return to primary navigation; current page, pins, canonical layout bytes and prior primary-origin metadata preserved; repeat migration and stale API/import settings cannot re-enable assignments.'
}finally{Remove-Item -LiteralPath $temp -Recurse -Force}
