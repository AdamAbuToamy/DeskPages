$ErrorActionPreference='Stop'
. (Join-Path (Split-Path $PSScriptRoot -Parent) 'StateEngine.ps1')
$root=Join-Path ([IO.Path]::GetTempPath()) ('dp-protected-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try{
 $script:DpStore=$root;$script:DpManifest=Join-Path $root 'pages.xml'
 $script:DpState=[PSCustomObject]@{Current=2;Pages=@(1,2);Items=@()};Save-DpState
 $original=(Get-FileHash $script:DpManifest).Hash
 function Connect-DpDesktop {$script:connected++}
 function Set-DpFilePage {param($Page,$Commit,$RegisterNew) if($Page -ne 2 -or $Commit -or $RegisterNew){throw 'Unsafe recovery arguments'}}
 function Set-DpSystemIcons {param($Page)}
 function Refresh-DpDesktop {}
 function Restore-DpPageLayout {param($Page) if($script:mode -eq 'positions'){throw 'Explorer rejected positions'};if($script:mode -eq 'display'){$script:revision++}}
 function Get-DpDisplaySignal {return @($script:revision,0,10000,0)}
 function Write-DpLog {param($Message)}
 foreach($mode in @('positions','display','success')){
  $script:mode=$mode;$script:revision=0;$script:connected=0;$script:DpSuspended=$true
  $failed=$false;try{Resume-DpProtectedSession}catch{$failed=$true}
  if($failed -ne ($mode -ne 'success') -or $script:DpSuspended -ne ($mode -ne 'success')){throw ('Unsafe suspension state: '+$mode)}
  if((Get-FileHash $script:DpManifest).Hash -ne $original -or $script:DpState.Current -ne 2){throw 'Recovery overwrote committed metadata'}
  Write-Host ('PASS: protected resume '+$mode+'; manifest unchanged')
 }
 $script:DpSuspended=$true;$script:connected=0
 [IO.File]::WriteAllText((Join-Path $root 'suspended.xml'),'explicit recovery')
 $failed=$false;try{Resume-DpProtectedSession}catch{$failed=$true}
 if(-not $failed -or -not $script:DpSuspended -or $script:connected -ne 0){throw 'Intentional show-all suspension was bypassed'}
 Remove-Item -LiteralPath (Join-Path $root 'suspended.xml')
 [IO.File]::WriteAllText($script:DpManifest,'corrupt');$script:connected=0
 $failed=$false;try{Resume-DpProtectedSession}catch{$failed=$true}
 if(-not $failed -or -not $script:DpSuspended -or $script:connected -ne 0){throw 'Corrupt manifest reached Explorer'}
 Write-Host 'PASS: explicit recovery mode and corrupt manifest remain protected; no live desktop touched.'
}finally{Remove-Item -LiteralPath $root -Recurse -Force}
