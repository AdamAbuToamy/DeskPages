param([string]$Engine)
$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
if(-not $Engine){$Engine=Join-Path $base 'StateEngine.ps1'}
. $Engine
$root=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-many-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try {
 $script:DpStore=$root;$script:DpManifest=Join-Path $root 'pages.xml';$script:DpSuspended=$false;$script:hostReady=$false
 $script:DpState=[PSCustomObject]@{Current=1;Pages=@(1,2,3,5);Items=@([PSCustomObject]@{Path='stable-a';Page=1},[PSCustomObject]@{Path='stable-b';Page=3})}
 $script:visible=1;$script:physical=1;$script:systemPage=1
 function Save-DpPageLayout {param($Page) if($script:visible -ne $Page){throw 'Page view contains another page; layout saving is blocked.'}}
 function Set-DpFilePage {param($Page,$Commit,$RegisterNew) $script:physical=$Page}
 function Set-DpSystemIcons {param($Page) $changed=($script:systemPage -eq 1) -ne ($Page -eq 1);$script:systemPage=$Page;return $changed}
 function Refresh-DpDesktop {$script:visible=$script:physical}
 function Restore-DpPageLayout {param($Page) if($script:visible -ne $Page){throw 'Explorer still shows icons from the previous page.'}}
 function Write-DpLog {param($Message)}
 Save-DpState
 foreach($page in @(2,3,5,1,5,3,2,1)){
  Invoke-DpPageTransaction -Page $page -Animate:$false
  if($script:DpState.Current -ne $page -or $script:visible -ne $page -or (Read-DpManifest $script:DpManifest).Current -ne $page){throw 'Page/view/manifest mismatch'}
 }
 Write-Host 'PASS: repeated navigation through 1,2,3,5 requires an explicit Explorer refresh even without namespace changes'
 $script:visible=2;$script:physical=2
 Invoke-DpPageTransaction -Page 3 -Animate:$false
 if($script:DpState.Current -ne 3 -or $script:visible -ne 3){throw 'Mixed source was not repaired from committed page'}
 if(($script:DpState.Pages -join ',') -ne '1,2,3,5' -or $script:DpState.Items[1].Page -ne 3){throw 'Repair changed page membership'}
 Write-Host 'PASS: stale source view repaired before saving; page IDs and memberships preserved'
}finally{Remove-Item -LiteralPath $root -Recurse -Force}
