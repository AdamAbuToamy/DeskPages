param([Parameter(Mandatory)][string]$Installer,[string]$ReportDirectory)
$ErrorActionPreference='Stop'
if(-not $ReportDirectory){$ReportDirectory=Join-Path (Split-Path $Installer -Parent) 'security-report'}
New-Item -ItemType Directory -Force $ReportDirectory | Out-Null
$report=[ordered]@{Verdict='NOT_SCANNED';InstallerExecuted=$false;SampleUploadPermitted=$false;Limitation='One isolated Windows Server scan is not a guarantee of all Windows 11 browser/cloud verdicts.'}
$started=Get-Date
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
 Update-MpSignature
 Set-MpPreference -MAPSReporting Advanced -DisableRealtimeMonitoring $false
 $pref=Get-MpPreference;$status=Get-MpComputerStatus
 $report.CloudPreferences=$pref | Select-Object MAPSReporting,SubmitSamplesConsent,DisableBlockAtFirstSeen
 $report.DefenderAfter=$status | Select-Object AMServiceEnabled,AntivirusEnabled,RealTimeProtectionEnabled,AMEngineVersion,AMProductVersion,AntivirusSignatureVersion,AntivirusSignatureLastUpdated
 if([int]$pref.SubmitSamplesConsent -ne 2 -or [int]$pref.MAPSReporting -eq 0 -or -not $status.RealTimeProtectionEnabled){throw 'Requested diagnostic protection state was not established.'}
 if($status.AntivirusSignatureLastUpdated -lt (Get-Date).AddDays(-2)){throw 'Signatures are stale.'}
 $platform=Get-ChildItem "$env:ProgramData/Microsoft/Windows Defender/Platform" -Directory | Sort-Object Name -Descending | Select-Object -First 1
 $scanner=Join-Path $platform.FullName 'MpCmdRun.exe'
 $previous=$ErrorActionPreference
 try{
  $ErrorActionPreference='Continue'
  $connection=(& $scanner -ValidateMapsConnection 2>&1 | Out-String);$connectionCode=$LASTEXITCODE
  $connection | Set-Content (Join-Path $ReportDirectory 'cloud-connection.txt') -Encoding UTF8
  $report.CloudConnectionExitCode=$connectionCode
  if($connectionCode -ne 0){throw 'Defender cloud connection validation failed.'}
  # Only Microsoft's scanner is executed; the installer is scanned as data.
  $output=(& $scanner -Scan -ScanType 3 -File $file.FullName 2>&1 | Out-String);$code=$LASTEXITCODE
 }finally{$ErrorActionPreference=$previous}
 $output | Set-Content (Join-Path $ReportDirectory 'scanner-output.txt') -Encoding UTF8
 $report.ScannerExitCode=$code
 $report.Detections=@(Get-MpThreatDetection | Where-Object {$_.InitialDetectionTime -ge $started -or ($_.Resources -join ' ') -like ('*'+$file.Name+'*')} | Select-Object ThreatID,InitialDetectionTime,ActionSuccess,Resources)
 $report.Threats=@(Get-MpThreat | Select-Object ThreatID,ThreatName,IsActive,DidThreatExecute,Resources)
 if($report.Detections.Count -or $report.Threats.Count -or -not(Test-Path -LiteralPath $file.FullName)){$report.Verdict='DETECTION_RECORDED';throw 'Security detection recorded; do not distribute the build.'}
 if($code -ne 0){$report.Verdict='SCAN_ERROR';throw ('Scanner returned '+$code)}
 $report.Verdict='NO_DETECTION_IN_THIS_SCAN'
}catch{$report.Error=$_.ToString();throw}
finally{
 $report.CompletedUtc=[DateTime]::UtcNow.ToString('o')
 $report | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $ReportDirectory 'report.json') -Encoding UTF8
 $report | ConvertTo-Json -Depth 10 | Write-Host
}
