$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
. (Join-Path $base 'StateEngine.ps1')
. (Join-Path $base 'Layouts.ps1')
$root=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-recover-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $root | Out-Null
try {
 $script:DpStore=$root;$script:DpLayoutStore=$root;$script:DpManifest=Join-Path $root 'pages.xml'
 $saved=Join-Path $root 'page-1.xml';[IO.File]::WriteAllText($saved,'original page 1 snapshot');$hash=(Get-FileHash $saved).Hash
 function Connect-DpDesktop {}
 function Save-DpPageLayout {param($Page) if($script:scenario -eq 'MixedSource'){throw 'Page view contains another page; layout saving is blocked.'}}
 function Set-DpHidden {param($Path,$Hidden) if($Hidden){throw 'Recovery must show files'};if($script:scenario -eq 'VisibilityFailure'){throw 'Injected access failure'}}
 function Set-DpSystemIcons {param($Page)}
 function Refresh-DpDesktop {}
 function Restore-DpMergedDesktop {$script:mergeCalled=$true;if($script:scenario -eq 'MergeFailure'){throw 'Injected coordinate verification failure'}}
 function Write-DpLog {param($Message)}
 foreach($case in @('GoodSource','MixedSource','VisibilityFailure','MergeFailure')){
  $script:scenario=$case;$script:DpSuspended=$false;$script:mergeCalled=$false
  $script:DpState=[PSCustomObject]@{Current=3;Pages=@(1,2,3);Items=@([PSCustomObject]@{Path='original-path';Page=3})};Save-DpState
  $failed=$false;try{Recover-DpDesktop}catch{$failed=$true}
  if($failed -ne ($case -in @('VisibilityFailure','MergeFailure'))){throw ('Wrong recovery result '+$case)}
  if(-not $script:DpSuspended -or $script:DpState.Current -ne 3 -or (Get-FileHash $saved).Hash -ne $hash){throw 'Show-all overwrote committed page or snapshot'}
  if($case -eq 'VisibilityFailure' -and $script:mergeCalled){throw 'Merged an incomplete view'}
  Write-Host ('PASS: recovery '+$case+' preserves original snapshots and committed page')
 }
}finally{Remove-Item -LiteralPath $root -Recurse -Force}
