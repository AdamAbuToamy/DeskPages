$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
. (Join-Path $base 'WorkspaceCommands.ps1')
Add-Type 'public static class DeskPagesAnimation {public static bool IsVisible {get{return false;}}}'
$script:DpSuspended=$false;$script:DpEdgeDrag=$null;$script:DpWorkspaceBusy=$false;$script:hostReady=$false
$script:DpState=[PSCustomObject]@{Current=1;Items=@(for($i=0;$i -lt 10000;$i++){[PSCustomObject]@{Page=1+($i%99);Path=('C:\CloudFixture\'+$i+'.txt')}});Workspace=[PSCustomObject]@{Order=@(1..99);Archived=@(3);Locked=@(7)}}
function Get-DpWorkspaceName {param($Page) return ('Page '+$Page)}
function Test-Path {throw 'Opening page cards must not query filesystem/cloud item availability.'}
$watch=[Diagnostics.Stopwatch]::StartNew()
$rows=@(Invoke-DpWorkspaceRequest -Command pages -Arguments @())
if($rows.Count -ne 99){throw 'Missing page rows.'}
$total=0
foreach($row in $rows){$fields=$row.Split("`t");$total+=[int]$fields[2];if($fields.Length -ne 6){throw 'Changed page model schema.'}}
if($total -ne 10000 -or $script:DpWorkspaceBusy){throw 'Incorrect tracked membership counts or busy guard not released.'}
Write-Host ('PASS: production workspace page listing counted 10,000 tracked memberships across 99 pages without any filesystem probe; elapsed '+$watch.ElapsedMilliseconds+' ms on this host (not Windows latency).')
