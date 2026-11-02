param([Parameter(Mandatory)][string]$Installer,[string]$ReportDirectory,[string]$PayloadRoot,[string]$PayloadInventory)
$ErrorActionPreference='Stop'
if(-not $ReportDirectory){$ReportDirectory=Join-Path (Split-Path $Installer -Parent) 'security-report'}
New-Item -ItemType Directory -Force $ReportDirectory | Out-Null
$report=[ordered]@{ScanMode='Explicit read-only custom scan; file exclusions ignored and archive contents scanned';Verdict='NOT_SCANNED';InstallerExecuted=$false;SampleUploadPermitted=$false;Limitation='One isolated Windows Server scan is not a guarantee of all Windows 11 browser/cloud verdicts.'}
$started=Get-Date;$payloadStage=$null
$report.PayloadVerdict='NOT_SCANNED';$report.PayloadFiles=0
try{
 $file=Get-Item -LiteralPath $Installer
 $report.File=$file.Name;$report.Sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
 $report.Authenticode=(Get-AuthenticodeSignature -LiteralPath $file.FullName).Status.ToString()
 $before=Get-MpComputerStatus
 $report.DefenderBefore=$before | Select-Object AMServiceEnabled,AntivirusEnabled,RealTimeProtectionEnabled,AntivirusSignatureVersion
 if(-not $before.AMServiceEnabled -or -not $before.AntivirusEnabled){throw 'Defender unavailable; no scan pass can be claimed.'}
 # Respect the user's instruction: do not submit the executable to Microsoft.
 # This disposable CI image already refuses sample uploads. Never weaken a
 # different machine's protection policy to make it fit this diagnostic job.
 $pref=Get-MpPreference
 if([int]$pref.SubmitSamplesConsent -ne 2){throw 'This scanner may submit samples; refused because the user did not authorize file submission.'}
 # Hosted Windows runners sometimes reject the Defender CIM signature update with
 # RPC 0x800706BE. Retry through the Defender CLI without relaxing scan gates.
 $platform=Get-ChildItem "$env:ProgramData/Microsoft/Windows Defender/Platform" -Directory | Sort-Object Name -Descending | Select-Object -First 1
 if(-not $platform){throw 'Defender platform unavailable; cannot scan.'}
 $scanner=Join-Path $platform.FullName 'MpCmdRun.exe'
 $signatureReady=$false
 for($attempt=0;$attempt -lt 3 -and -not $signatureReady;$attempt++){
  try{Update-MpSignature -ErrorAction Stop;$signatureReady=$true}
  catch{
   $report.SignatureUpdateError=$_.Exception.Message
   $cliOutput=(& $scanner -SignatureUpdate 2>&1 | Out-String)
   $cliCode=$LASTEXITCODE
   $report.SignatureCliExitCode=$cliCode
   $cliOutput | Set-Content (Join-Path $ReportDirectory 'signature-update.txt') -Encoding UTF8
   if($cliCode -eq 0){$signatureReady=$true}
   elseif($attempt -lt 2){Start-Sleep -Seconds 3}
  }
 }
 if(-not $signatureReady){throw 'Defender signature update unavailable after PowerShell and CLI retries; scan not approved.'}
 # The hosted Defender WMI provider crashes after signature update.
 # Keep the immutable pre-update privacy preferences; verify them without writes.
 # A successful direct CLI update and BOTH explicit CLI scans are mandatory.
 $report.CloudPreferences=$pref | Select-Object MAPSReporting,SubmitSamplesConsent,DisableBlockAtFirstSeen,DisableArchiveScanning,DisableScriptScanning,ExclusionPath
 if([int]$pref.SubmitSamplesConsent -ne 2 -or [int]$pref.MAPSReporting -eq 0 -or $pref.DisableArchiveScanning -or $pref.DisableScriptScanning){throw 'Safe Defender preferences were not confirmed before scanning.'}
 $report.DefenderAfter='CIM not queried because hosted provider fails after update; CLI update and scans must succeed.'
 $previous=$ErrorActionPreference
 try{
  $ErrorActionPreference='Continue'
  $connection='';$connectionCode=-1
  for($attempt=0;$attempt -lt 3;$attempt++){
   $connection+=(& $scanner -ValidateMapsConnection 2>&1 | Out-String);$connectionCode=$LASTEXITCODE
   if($connectionCode -eq 0){break};if($attempt -lt 2){Start-Sleep -Milliseconds 500}
  }
  $connection | Set-Content (Join-Path $ReportDirectory 'cloud-connection.txt') -Encoding UTF8
  $report.CloudConnectionExitCode=$connectionCode
  if($connectionCode -ne 0){$report.CloudValidationError='Cloud connection unavailable; perform the direct scan and keep the release gate blocked.'}
  # This flag makes a custom scan ignore file exclusions and scan archives.
  # It does not disable antivirus protection; no sample/installer is executed.
  # Findings remain in command output and cause the delivery gate to fail.
  # https://learn.microsoft.com/defender-endpoint/command-line-arguments-microsoft-defender-antivirus
  $output=(& $scanner -Scan -ScanType 3 -DisableRemediation -File $file.FullName 2>&1 | Out-String);$code=$LASTEXITCODE
 }finally{$ErrorActionPreference=$previous}
 $output | Set-Content (Join-Path $ReportDirectory 'scanner-output.txt') -Encoding UTF8
 $report.ScannerExitCode=$code
 # Inspect the actual file set assembled by the packager, not only the EXE container.
 if(-not $PayloadRoot -or -not $PayloadInventory){throw 'Payload root and build inventory are required for a distribution scan.'}
 . (Join-Path $PSScriptRoot 'SecurityPayload.ps1')
 $payloadStage=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-security-'+[guid]::NewGuid().ToString('N'))
 $staged=Copy-DpSecurityPayload -PayloadRoot $PayloadRoot -PayloadInventory $PayloadInventory -PayloadStage $payloadStage
 $entries=$staged.Entries
 $report.PayloadInventorySha256=(Get-FileHash -LiteralPath $PayloadInventory -Algorithm SHA256).Hash.ToLowerInvariant()
 $report.PayloadFiles=$staged.Files
 $previous=$ErrorActionPreference
 try{$ErrorActionPreference='Continue';$payloadOutput=(& $scanner -Scan -ScanType 3 -DisableRemediation -File $payloadStage 2>&1 | Out-String);$payloadCode=$LASTEXITCODE}finally{$ErrorActionPreference=$previous}
 $payloadOutput | Set-Content (Join-Path $ReportDirectory 'payload-scanner-output.txt') -Encoding UTF8
 $report.PayloadScannerExitCode=$payloadCode
 if($payloadCode -ne 0 -or $payloadOutput -match '(?im)^\s*Threat\s*:'){$report.PayloadVerdict='SCAN_ERROR_OR_DETECTION';throw 'Payload scan did not pass; do not distribute.'}
 foreach($entry in $entries){$target=Join-Path $payloadStage $entry.Path;if(-not(Test-Path -LiteralPath $target) -or (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ine $entry.Sha256){throw ('Payload was removed or changed during scanning: '+$entry.Path)}}
 $report.PayloadVerdict='NO_DETECTION_IN_THIS_SCAN';$report.PayloadHashesVerified=$true
 # Defender CLI owns the verdict when its hosted WMI provider is unavailable.
 # Do not manufacture an empty threat list from a failed WMI query.
 if(-not(Test-Path -LiteralPath $file.FullName)){throw 'Installer disappeared after scanning.'}
 if($code -ne 0 -or $output -match '(?im)^\s*Threat\s*:'){$report.Verdict='SCAN_ERROR';throw ('Scanner returned '+$code)}
 if($connectionCode -ne 0){$report.Verdict='NO_LOCAL_DETECTION_CLOUD_UNAVAILABLE';throw 'Direct scan completed with no detection; cloud validation remains unavailable.'}
 $report.Verdict='NO_DETECTION_IN_THIS_SCAN'
}catch{$report.Error=$_.ToString();throw}
finally{
 if($payloadStage -and (Test-Path -LiteralPath $payloadStage)){Remove-Item -LiteralPath $payloadStage -Recurse -Force -ErrorAction SilentlyContinue}
 $report.CompletedUtc=[DateTime]::UtcNow.ToString('o')
 $report | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $ReportDirectory 'report.json') -Encoding UTF8
 $report | ConvertTo-Json -Depth 10 | Write-Host
}
