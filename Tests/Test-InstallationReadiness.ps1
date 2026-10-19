$ErrorActionPreference='Stop'
. (Join-Path (Split-Path $PSScriptRoot -Parent) 'InstallationReadiness.ps1')
$root=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-ready-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $root | Out-Null
try {
 $token=[Guid]::NewGuid().ToString('N');$path=Join-Path $root 'receipt.txt';$alive=[PSCustomObject]@{HasExited=$false}
 [IO.File]::WriteAllText($path,"DeskPagesInstallReady1`n$token`nAnimation=True")
 Wait-DpInstalledAgent -Process $alive -ReadyPath $path -Token $token
 if(Test-Path -LiteralPath $path){throw 'Receipt not consumed'}
 Write-Host 'PASS: installation success requires matching live-agent readiness and enabled animation'
 foreach($case in @('WrongToken','DisabledAnimation','Exited','MissingReceipt')){
  [IO.File]::Delete($path);$process=$alive
  if($case -ne 'MissingReceipt'){
   $value=$token;$enabled='True';if($case -eq 'WrongToken'){$value='other'};if($case -eq 'DisabledAnimation'){$enabled='False'}
   [IO.File]::WriteAllText($path,"DeskPagesInstallReady1`n$value`nAnimation=$enabled")
  }
  if($case -eq 'Exited'){$process=[PSCustomObject]@{HasExited=$true}}
  $blocked=$false;try{Wait-DpInstalledAgent -Process $process -ReadyPath $path -Token $token -TimeoutSeconds 0.01}catch{$blocked=$true}
  if(-not $blocked){throw ('False installation success: '+$case)}
  Write-Host ('PASS: installation success blocked: '+$case)
 }
}finally{Remove-Item -LiteralPath $root -Recurse -Force}
