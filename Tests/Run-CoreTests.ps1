[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
. (Join-Path $base 'StateEngine.ps1')
$exe=[Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
$root=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-core-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$passed=0
function Assert-DpTest {param([bool]$Condition,[string]$Label)
    if (-not $Condition) {throw ('FAILED: '+$Label)}
}
function Invoke-DpChildTest {
    param([string]$Case,[string]$Mode,[string]$Stage)
    $start=[Diagnostics.ProcessStartInfo]::new()
    $start.FileName=$exe
    $start.Arguments='-NoProfile -File "'+(Join-Path $PSScriptRoot 'CrashWorker.ps1')+'" -Root "'+$Case+'" -Mode '+$Mode+' -Stage '+$Stage
    $start.UseShellExecute=$false;$start.CreateNoWindow=$true
    $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    $process=[Diagnostics.Process]::new();$process.StartInfo=$start
    try {
        $null=$process.Start()
        $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
        if(-not $process.WaitForExit(20000)){$process.Kill();throw 'A child test timed out.'}
        [PSCustomObject]@{Code=$process.ExitCode;Output=($stdout.Result+$stderr.Result)}
    } finally {$process.Dispose()}
}
try {
    foreach ($stage in @('JournalWritten','VisibilityApplied','SystemApplied','LayoutApplied','ManifestCommitted','JournalRemoved','PermissionDenied','SystemFailure','LayoutFailure','None')) {
        $case=Join-Path $root $stage;New-Item -ItemType Directory -Path $case | Out-Null
        $content=Join-Path $case 'file.txt';[IO.File]::WriteAllText($content,'do not move, hide by attribute only')
        $originalHash=(Get-FileHash -LiteralPath $content).Hash
        $state=[PSCustomObject]@{Current=1;Pages=@(1,2);Items=@([PSCustomObject]@{Path=$content;Page=1})}
        Write-DpAtomicXml -Path (Join-Path $case 'pages.xml') -Value $state
        foreach ($name in @('visibility','system','view')) {Write-DpAtomicXml -Path (Join-Path $case ($name+'.xml')) -Value ([PSCustomObject]@{Page=1})}
        $child=Invoke-DpChildTest -Case $case -Mode Switch -Stage $stage
        $code=$child.Code
        if ($stage -in @('JournalWritten','VisibilityApplied','SystemApplied','LayoutApplied','ManifestCommitted','JournalRemoved')) {
            Assert-DpTest ($code -eq 86) ('process really terminated at '+$stage)
        } elseif ($stage -in @('PermissionDenied','SystemFailure','LayoutFailure')) {
            Assert-DpTest ($code -ne 0) ('failure reported at '+$stage)
        } else {Assert-DpTest ($code -eq 0) 'normal transition'}
        $expected=1
        if ($stage -in @('ManifestCommitted','JournalRemoved','None')) {$expected=2}
        Assert-DpTest ((Read-DpManifest (Join-Path $case 'pages.xml')).Current -eq $expected) ('durable commit at '+$stage)
        $child=Invoke-DpChildTest -Case $case -Mode Resume -Stage None
        Assert-DpTest ($child.Code -eq 0) ('fresh-process resume at '+$stage+': '+$child.Output)
        foreach ($name in @('visibility','system','view')) {
            Assert-DpTest ((Import-Clixml -LiteralPath (Join-Path $case ($name+'.xml'))).Page -eq $expected) ($name+' restored at '+$stage)
        }
        Assert-DpTest (-not (Test-Path -LiteralPath (Join-Path $case 'transition.xml'))) ('journal resolved at '+$stage)
        Assert-DpTest ((Get-FileHash -LiteralPath $content).Hash -eq $originalHash) ('file bytes/path preserved at '+$stage)
        Assert-DpTest ((Read-DpManifest (Join-Path $case 'pages.xml')).Items[0].Page -eq 1) ('membership unchanged at '+$stage)
        $passed++;Write-Host ('PASS: '+$stage)
    }
    $manifest=Join-Path $root 'corrupt.xml'
    Write-DpAtomicXml -Path $manifest -Value ([PSCustomObject]@{Current=1})
    [IO.File]::WriteAllText($manifest,'<broken')
    $blocked=$false;try {$null=Read-DpManifest $manifest} catch {$blocked=$true}
    Assert-DpTest $blocked 'corrupt manifest fails closed'
    $passed++;Write-Host 'PASS: corrupt manifest fails closed'
    $script:DpStore=$root;$script:DpState=[PSCustomObject]@{Current=2};$script:DpSuspended=$false
    Suspend-DpSession
    Assert-DpTest $script:DpSuspended 'recovery blocks saves and switches'
    Assert-DpTest ((Import-Clixml (Join-Path $root 'suspended.xml')).Current -eq 2) 'recovery preserves active page'
    Assert-DpTest ($script:DpState.Current -eq 2) 'recovery does not replace current with page 1'
    $passed++;Write-Host 'PASS: recovery suspension preserves page'
    Write-Host ("PASS: $passed core scenarios; child-process termination and fresh-process recovery executed.") -ForegroundColor Green
    Write-Host 'Simulated adapters are NOT an actual Windows reboot, Explorer or touchpad test.'
} finally {Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue}
