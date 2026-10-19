$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
Add-Type -Path (Join-Path $base 'Native/DeskPagesPageNames.cs')
Add-Type @'
public sealed class ScopeSpacing {public int X=80,Y=100;}
public static class DeskPagesLayout {public static ScopeSpacing Spacing(object desktop){return new ScopeSpacing();}}
public static class DeskPagesAnimation {public static bool IsVisible {get{return false;}}}
public static class OriginalDesktopReader {public static object[] Icons;public static object[] Read(object desktop){return Icons;}}
'@
. (Join-Path $base 'StateEngine.ps1');. (Join-Path $base 'Workspace.ps1');. (Join-Path $base 'WorkspaceCommands.ps1');. (Join-Path $base 'NewItemPlacement.ps1')
function Save-DpVerifiedSourceLayout {param($Page)}
function Publish-DpWorkspacePreferences {}
function Write-DpLog {param($Message)}
# Isolated fixture identity on every OS; no production COM/native type is loaded.
function Get-DpWorkspaceIdentity {param([string]$Path) if(Test-Path -LiteralPath $Path){return [IO.File]::GetCreationTimeUtc($Path).Ticks.ToString()};return ''}
function Get-DpMonitorGeometry {return $script:screens}
function Resume-DpCommittedPage {
 $page=[int]$script:DpState.Current
 $script:visible=@($script:DpState.Items | Where-Object {Test-DpItemVisible $_ $page} | ForEach-Object {$_.Path})
 $saved=@(foreach($file in Get-ChildItem $script:DpLayoutStore -Filter 'page-*.xml'){@((Import-Clixml $file.FullName).Icons)})
 $script:positions=@(Get-DpScopedLayoutIcons $saved @() | Where-Object {$_.Key -in $script:visible})
}
function Assert($Pass,$Reason){if(-not $Pass){throw $Reason}}
$temp=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-scope-commit-'+[guid]::NewGuid().ToString('N'))
try{
 $script:DpDesktop=Join-Path $temp 'Desktop';$script:DpPublic=Join-Path $temp 'Public';$script:DpStore=Join-Path $temp 'Store';$script:DpManifest=Join-Path $script:DpStore 'pages.xml';$script:DpLayoutStore=Join-Path $temp 'Layouts'
 New-Item -ItemType Directory -Force $script:DpDesktop,$script:DpPublic,$script:DpStore,$script:DpLayoutStore | Out-Null
 $a=Join-Path $script:DpDesktop 'a.txt';$b=Join-Path $script:DpPublic 'b.txt'
 foreach($path in @($a,$b)){[IO.File]::WriteAllText($path,'unchanged')}
 $hash=(Get-FileHash $b).Hash
 $script:DpState=[PSCustomObject]@{Desktop=$script:DpDesktop;Current=1;PageCount=3;Pages=@(1,2,3);Items=@([PSCustomObject]@{Path=$a;Page=1},[PSCustomObject]@{Path=$b;Page=1});SystemIconBackup=@()}
 Initialize-DpWorkspace;Save-DpState
 $script:screens=@([PSCustomObject]@{Device='main';Left=0;Top=0;Right=1920;Bottom=1080;Primary=$true},[PSCustomObject]@{Device='second';Left=1920;Top=0;Right=3840;Bottom=1080;Primary=$false});$script:DpScopeScreens=$script:screens
 $icons=@([PSCustomObject]@{Key=$a;Name='a';X=17;Y=2},[PSCustomObject]@{Key=$b;Name='b';X=2000;Y=100})
 [OriginalDesktopReader]::Icons=$icons
 foreach($page in @(1,2,3)){Write-DpAtomicXml (Join-Path $script:DpLayoutStore "page-$page.xml") ([PSCustomObject]@{Page=$page;Icons=$icons})}
 # Real dispatcher and workspace journalling, with only the Explorer adapter replaced.
 Invoke-DpWorkspaceRequest 'display-scope' @('True')
 Assert ((Read-DpManifest $script:DpManifest).Workspace.PrimaryOnly) 'Scope command was not persisted.'
 Assert ($script:DpState.Items[1].Page -eq 1 -and -not $script:DpState.Items[1].PSObject.Properties['Pinned']) 'Scope mutated file page or user pin.'
 $script:DpState.Current=2;Save-DpState;Resume-DpCommittedPage
 Assert ($b -in $script:visible -and $a -notin $script:visible) 'Only primary content should page.'
 Assert (@($script:positions | Where-Object {$_.Key -eq $b -and $_.X -eq 2000 -and $_.Y -eq 100}).Count -eq 1) 'Secondary layout moved during scope command.'
 $manifest=(Get-FileHash $script:DpManifest).Hash
 $failed=$false;try{Invoke-DpWorkspaceRequest 'display-scope' @('invalid')}catch{$failed=$true};Assert ($failed -and (Get-FileHash $script:DpManifest).Hash -eq $manifest) 'Invalid scope changed state.'
 # Inject a failure before the new manifest is committed; recovery must agree.
 function Invoke-DpFaultPoint {param($Stage) if($Stage -eq 'WorkspaceLayouts'){throw 'Injected scope commit failure.'}}
 $failed=$false;try{Invoke-DpWorkspaceRequest 'display-scope' @('False')}catch{$failed=$true}
 Assert ($failed -and $script:DpState.Workspace.PrimaryOnly -and (Read-DpManifest $script:DpManifest).Workspace.PrimaryOnly) 'Scope failure left split durable state.'
 Assert ($b -in $script:visible -and -not(Test-Path (Join-Path $script:DpWorkspaceRoot 'pending.xml'))) 'Scope recovery lost secondary view or left journal.'
 function Invoke-DpFaultPoint {param($Stage)}
 Invoke-DpWorkspaceRequest 'display-scope' @('False')
 Assert (-not $script:DpState.Workspace.PrimaryOnly -and $b -notin $script:visible) 'Disabling scope did not resume assigned-page visibility.'
 Assert ((Get-FileHash $b).Hash -eq $hash) 'Scope commit/recovery changed file content or path.'
 Write-Host 'PASS: production display-scope dispatcher and durable workspace commit, three pages, secondary layout/visibility, invalid command, injected precommit failure/recovery, disable, unchanged real file and permanent pin ownership. Explorer rendering mocked.'
}finally{Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue}
