$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
. (Join-Path $base 'StateEngine.ps1')
. (Join-Path $base 'Layouts.ps1')
. (Join-Path $base 'RestartVerification.ps1')
$root=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-restart-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try {
    $script:DpStore=$root
    $file=Join-Path $root 'file.txt';[IO.File]::WriteAllText($file,'original')
    $script:DpState=[PSCustomObject]@{Current=1;Pages=@(1,2);Items=@([PSCustomObject]@{Path=$file;Page=1})}
    $script:testBoot='before'
    function Get-DpBootIdentity {$script:testBoot}
    $script:testFile=$file
    function Read-DpVisibleIcons {[PSCustomObject]@{Key=$script:testFile}}
    function Write-DpLog {param($Message) throw $Message}
    $record=[PSCustomObject]@{Boot='before';Current=1;Pages=@(1,2);Files=@(Get-DpRestartFiles);At=[DateTime]::UtcNow}
    $baseline=Join-Path $root 'restart-baseline.xml'
    $report=Join-Path $root 'restart-report.txt'
    Write-DpAtomicXml -Path $baseline -Value $record
    Complete-DpRestartCheck
    if(Test-Path $report){throw 'Same boot incorrectly certified as a reboot.'}
    Write-Host 'PASS: same boot cannot pass a restart check'
    $script:testBoot='after'
    Complete-DpRestartCheck
    if((Get-Content $report -Raw) -notmatch 'PASS: page'){throw ('Matching reboot fixture did not pass: '+(Get-Content $report -Raw))}
    Write-Host 'PASS: matching reboot fixture verified'
    $script:DpState.Current=2
    Write-DpAtomicXml -Path $baseline -Value $record
    Complete-DpRestartCheck
    if((Get-Content $report -Raw) -notmatch 'FAIL / CHANGED'){throw 'Changed active page incorrectly passed.'}
    Write-Host 'PASS: changed active page fails restart verification'
    $script:DpState.Current=1
    $record.Current=2;$record | Add-Member -NotePropertyName ExpectedStartupPage -NotePropertyValue 1
    Write-DpAtomicXml -Path $baseline -Value $record
    Complete-DpRestartCheck
    if((Get-Content $report -Raw) -notmatch 'PASS: page'){throw 'Expected main-page policy failed'}
    Write-Host 'PASS: reboot from page 2 expects page 1 under main-page policy'
    [IO.File]::WriteAllText($file,'changed file content and length')
    Write-DpAtomicXml -Path $baseline -Value $record
    Complete-DpRestartCheck
    if((Get-Content $report -Raw) -notmatch 'Size/time changed'){throw 'Changed file metadata incorrectly passed.'}
    Write-Host 'PASS: changed file metadata is reported'
    Write-DpAtomicXml -Path $baseline -Value $record
    Write-DpRestartFailure 'Injected startup permission failure'
    if((Get-Content $report -Raw) -notmatch '^FAIL:'){throw 'Startup failure did not leave a FAIL report.'}
    Write-Host 'PASS: failed startup cannot leave a stale successful report'
} finally {Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue}
