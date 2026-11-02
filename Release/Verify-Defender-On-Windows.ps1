param(
 [Parameter(Mandatory)][string]$Installer,
 [Parameter(Mandatory)][string]$ExpectedSha256,
 [string]$ReportDirectory = (Join-Path $env:TEMP 'DeskPages-Defender-Verification')
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$file=(Resolve-Path -LiteralPath $Installer).Path
$hash=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
if($hash -ne $ExpectedSha256.ToLowerInvariant()){throw 'Installer SHA-256 does not match the expected build. Refusing to scan a different binary.'}
$service=Get-Service -Name WinDefend -ErrorAction Stop
if($service.Status -ne 'Running'){throw 'Defender Antivirus service is not running. Use a Windows machine with active Microsoft Defender.'}
$status=Get-MpComputerStatus
if(-not $status.AMServiceEnabled -or -not $status.AntivirusEnabled){throw 'Microsoft Defender Antivirus is not available.'}
$preferences=Get-MpPreference
if([int]$preferences.SubmitSamplesConsent -ne 2){throw 'Defender automatic sample submission is not disabled. Do not run without authorization.'}
Update-MpSignature -ErrorAction Stop
$status=Get-MpComputerStatus
if($status.AntivirusSignatureLastUpdated -lt (Get-Date).AddDays(-2)){throw 'Defender signatures are not current.'}
$scanStarted=Get-Date
$scanOutput=Join-Path $ReportDirectory 'scan.txt'
Start-MpScan -ScanType CustomScan -ScanPath $file -ErrorAction Stop
$findings=@(Get-MpThreatDetection | Where-Object {$_.InitialDetectionTime -ge $scanStarted})
$report=[ordered]@{
 Result=$(if($findings.Count -eq 0){'NO_DETECTION_IN_THIS_SCAN'}else{'DETECTION_RECORDED'})
 Scanner='Microsoft Defender Antivirus'
 File=(Split-Path $file -Leaf)
 Sha256=$hash
 SignatureVersion=$status.AntivirusSignatureVersion
 SignatureUpdated=$status.AntivirusSignatureLastUpdated
 ScanStarted=$scanStarted.ToUniversalTime().ToString('o')
 ScanCompleted=[DateTime]::UtcNow.ToString('o')
 Detections=$findings.Count
}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $ReportDirectory 'report.json') -Encoding UTF8
$report | ConvertTo-Json -Depth 5 | Write-Host
if($findings.Count -gt 0){throw 'Defender reported a detection. Do not distribute.'}
