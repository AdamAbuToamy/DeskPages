$ErrorActionPreference='Stop'
. (Join-Path (Split-Path $PSScriptRoot -Parent) 'StateEngine.ps1')
$root=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-refresh-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try {
 function Save-DpPageLayout {param($Page)}
 function Set-DpFilePage {param($Page,$Commit,$RegisterNew)}
 function Set-DpSystemIcons {param($Page) return $script:namespaceChanged}
 function Refresh-DpDesktop {$script:refreshes++}
 function Restore-DpPageLayout {param($Page)
  if($script:refreshes -eq 0){throw 'No explicit refresh before layout'}
  if($script:reject -and $Page -eq 2){throw 'Injected target rejection'}
 }
 function Write-DpLog {param($Message)}
 foreach($case in @('FilesOnly','NamespaceChange','RejectedLayout')){
  $script:DpStore=$root;$script:DpManifest=Join-Path $root 'pages.xml';$script:DpState=[PSCustomObject]@{Current=1;Pages=@(1,2,3);Items=@()}
  $script:DpSuspended=$false;$script:hostReady=$false;$script:refreshes=0;$script:namespaceChanged=$case -eq 'NamespaceChange';$script:reject=$case -eq 'RejectedLayout'
  Save-DpState;$failed=$false;try{Invoke-DpPageTransaction -Page 2 -Animate:$false}catch{$failed=$true}
  if($script:reject){if(-not $failed -or $script:DpState.Current -ne 1 -or $script:refreshes -ne 2){throw 'Rejected layout was committed or rollback not refreshed'}}
  else{if($failed -or $script:refreshes -ne 1 -or $script:DpState.Current -ne 2){throw ('Refresh policy failed '+$case)}}
  Write-Host ('PASS: refresh '+$case)
 }
}finally{Remove-Item -LiteralPath $root -Recurse -Force}
