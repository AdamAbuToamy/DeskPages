[CmdletBinding()]
param([ValidateRange(15,600)][int]$Seconds=60,[ValidateRange(5,60)][int]$WarmupSeconds=15)
$ErrorActionPreference='Stop'
if([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT){throw 'Run this collector on Windows with DeskPages running.'}
$store=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'DeskPages\HiddenPages'
$pidFile=Join-Path $store 'performance-agent.pid'
if(-not(Test-Path -LiteralPath $pidFile)){throw 'Start DeskPages before measuring.'}
$identity=@(Get-Content -LiteralPath $pidFile)
if($identity.Count -ne 2){throw 'Restart the preview agent before measuring.'}
$agentPid=[int]$identity[0]
$agent=Get-Process -Id $agentPid -ErrorAction Stop
$agentStarted=$agent.StartTime.ToUniversalTime()
if($agentStarted.Ticks -ne [long]$identity[1]){throw 'The agent receipt is stale. Restart DeskPages.'}
$folder=Join-Path ([Environment]::GetFolderPath('MyDocuments')) ('DeskPages-Performance-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $folder | Out-Null
$modeFile=Join-Path $store 'capture-settings.txt';$original=$false
if(Test-Path -LiteralPath $modeFile){$original=[bool]::Parse((Get-Content -LiteralPath $modeFile -Raw).Trim())}
$request=Join-Path $store 'capture-mode.request'
$mutex=New-Object Threading.Mutex($false,'Local\DeskPagesPerformanceComparison')
if(-not $mutex.WaitOne(0)){throw 'Another comparison is already running.'}
function Set-MeasurementMode([bool]$Enabled){
 [IO.File]::WriteAllText($request,$Enabled.ToString())
 $wait=[Diagnostics.Stopwatch]::StartNew()
 while($wait.Elapsed.TotalSeconds -lt 20){
  $now=Get-Process -Id $agentPid -ErrorAction Stop
  if($now.StartTime.ToUniversalTime() -ne $agentStarted){throw 'The agent restarted. Start a new measurement.'}
  if(-not(Test-Path -LiteralPath $request) -and (Test-Path -LiteralPath $modeFile) -and [bool]::Parse((Get-Content -LiteralPath $modeFile -Raw).Trim()) -eq $Enabled){return}
  Start-Sleep -Milliseconds 250
 }
 throw 'DeskPages did not acknowledge the requested capture mode.'
}
function Get-MeasuredProcesses {
 $all=@(Get-CimInstance Win32_Process | Select-Object ProcessId,ParentProcessId)
 $ids=New-Object 'Collections.Generic.HashSet[int]';[void]$ids.Add($agentPid)
 do{$added=$false;foreach($entry in $all){if([int]$entry.ProcessId -ne $PID -and $ids.Contains([int]$entry.ParentProcessId) -and $ids.Add([int]$entry.ProcessId)){$added=$true}}}while($added)
 @(foreach($idValue in $ids){Get-Process -Id $idValue -ErrorAction SilentlyContinue})
}
$rows=New-Object 'Collections.Generic.List[object]';$summaries=New-Object 'Collections.Generic.List[object]';$windows=New-Object 'Collections.Generic.List[object]'
try{
 Write-Host 'DeskPages: direct capture versus cached capture' -ForegroundColor Cyan
 Write-Host 'Use the same pages and navigation sequence in both phases. Keep other workload and displays unchanged.'
 Write-Host 'Images are cached in RAM and encrypted local snapshot files. This report contains timings and counters, not screenshots or desktop filenames.'
 foreach($enabled in @($false,$true)){
  $mode=if($enabled){'cached'}else{'direct'}
  Set-MeasurementMode $enabled
  Write-Host "`nMode: $mode. Visit every test page once, then press Enter." -ForegroundColor Yellow
  [void](Read-Host)
  Write-Host "Warmup: $WarmupSeconds seconds. Switch between the same pages now."
  for($i=0;$i -lt $WarmupSeconds;$i++){Start-Sleep -Seconds 1}
  Write-Host "Recording $mode for $Seconds seconds. Repeat your navigation sequence." -ForegroundColor Green
  $start=[DateTime]::UtcNow;$clock=[Diagnostics.Stopwatch]::StartNew();$last=@{};$lastAt=$clock.Elapsed.TotalMilliseconds
  foreach($process in @(Get-MeasuredProcesses)){$last[[string]$process.Id]=$process.TotalProcessorTime.TotalMilliseconds}
  while($clock.Elapsed.TotalSeconds -lt $Seconds){
   Start-Sleep -Seconds 1
   $stillRunning=Get-Process -Id $agentPid -ErrorAction Stop
   if($stillRunning.StartTime.ToUniversalTime() -ne $agentStarted){throw 'The agent restarted during measurement. Discard this comparison.'}
   $at=$clock.Elapsed.TotalMilliseconds;$cpu=0.0;$private=0L;$working=0L;$handles=0;$processes=@(Get-MeasuredProcesses)
   foreach($process in $processes){try{$process.Refresh();$key=[string]$process.Id;$currentCpu=$process.TotalProcessorTime.TotalMilliseconds;if($last.ContainsKey($key)){$cpu+=[Math]::Max(0,$currentCpu-$last[$key])};$last[$key]=$currentCpu;$private+=$process.PrivateMemorySize64;$working+=$process.WorkingSet64;$handles+=$process.HandleCount}catch{}}
   $cache=0L;$cachePath=Join-Path $store 'snapshot-resources.txt';if(Test-Path -LiteralPath $cachePath){[void][long]::TryParse((Get-Content -LiteralPath $cachePath -Raw).Trim(),[ref]$cache)}
   $oneCore=100*$cpu/[Math]::Max(1,($at-$lastAt));$lastAt=$at
   $rows.Add([PSCustomObject]@{Utc=[DateTime]::UtcNow.ToString('o');Mode=$mode;CpuMachinePercent=$oneCore/[Environment]::ProcessorCount;CpuOneCorePercent=$oneCore;PrivateBytes=$private;WorkingSetSumBytes=$working;CacheBytes=$cache;Handles=$handles;Processes=$processes.Count})
  }
  $end=[DateTime]::UtcNow;$windows.Add([PSCustomObject]@{Mode=$mode;StartUtc=$start;EndUtc=$end})
  $phase=@($rows | Where-Object Mode -eq $mode)
  $summaries.Add([PSCustomObject]@{Mode=$mode;Samples=$phase.Count;MeanCpuMachinePercent=($phase | Measure-Object CpuMachinePercent -Average).Average;PeakPrivateMiB=($phase | Measure-Object PrivateBytes -Maximum).Maximum/1MB;PeakWorkingSetSumMiB=($phase | Measure-Object WorkingSetSumBytes -Maximum).Maximum/1MB;PeakSnapshotMiB=($phase | Measure-Object CacheBytes -Maximum).Maximum/1MB})
 }
 $rows | Export-Csv -LiteralPath (Join-Path $folder 'resource-samples.csv') -NoTypeInformation -Encoding UTF8
 $summaries | Export-Csv -LiteralPath (Join-Path $folder 'summary.csv') -NoTypeInformation -Encoding UTF8
 $navigation=Join-Path $store 'transition-performance-v3.csv'
 $latency=@();if(Test-Path -LiteralPath $navigation){$all=Import-Csv -LiteralPath $navigation;foreach($window in $windows){$latency+=@($all | Where-Object {$_.Mode -eq $window.Mode -and [DateTime]::Parse($_.Utc).ToUniversalTime() -ge $window.StartUtc -and [DateTime]::Parse($_.Utc).ToUniversalTime() -le $window.EndUtc})}}
 if($latency.Count){$latency | Export-Csv -LiteralPath (Join-Path $folder 'transition-samples.csv') -NoTypeInformation -Encoding UTF8}
 $latencySummary=@(foreach($mode in @('direct','cached')){$values=@($latency | Where-Object {$_.Mode -eq $mode -and $_.Success -eq 'True'} | ForEach-Object {[double]$_.CommandToSlideMs} | Sort-Object);if($values.Count){$ready=@($latency | Where-Object {$_.Mode -eq $mode -and $_.Success -eq 'True'} | ForEach-Object {[double]$_.DesktopReadyMs} | Sort-Object);[PSCustomObject]@{Mode=$mode;MedianDesktopReadyMs=$ready[[int][Math]::Floor(($ready.Count-1)*.5)];P95DesktopReadyMs=$ready[[Math]::Max(0,[int][Math]::Ceiling($ready.Count*.95)-1)];Navigations=$values.Count;CachedPreviews=@($latency | Where-Object {$_.Mode -eq $mode -and $_.Success -eq 'True' -and $_.CachedPreview -eq 'True'}).Count;MedianCommandToSlideMs=$values[[int][Math]::Floor(($values.Count-1)*.5)];P95CommandToSlideMs=$values[[Math]::Max(0,[int][Math]::Ceiling($values.Count*.95)-1)]}}})
 if($latencySummary.Count){$latencySummary | Export-Csv -LiteralPath (Join-Path $folder 'latency-summary.csv') -NoTypeInformation -Encoding UTF8}
 Add-Type -AssemblyName System.Windows.Forms
 $hardware=[PSCustomObject]@{CPU=@(Get-CimInstance Win32_Processor | Select-Object Name,NumberOfCores,NumberOfLogicalProcessors);PhysicalMemoryBytes=(Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory;OS=Get-CimInstance Win32_OperatingSystem | Select-Object Caption,Version,BuildNumber;Displays=@([Windows.Forms.Screen]::AllScreens | ForEach-Object {[PSCustomObject]@{Device=$_.DeviceName;Primary=$_.Primary;Left=$_.Bounds.Left;Top=$_.Bounds.Top;Width=$_.Bounds.Width;Height=$_.Bounds.Height}})}
 [PSCustomObject]@{Hardware=$hardware;RecordedUtc=[DateTime]::UtcNow;Windows=[Environment]::OSVersion.VersionString;LogicalProcessors=[Environment]::ProcessorCount;SecondsPerPhase=$Seconds;WarmupSeconds=$WarmupSeconds;AgentPid=$agentPid;Notes=@('CPU normalized by logical processors; one-core CPU also retained.','Private bytes sum is process commitment; working-set sum double counts shared pages.','Main agent and observed descendants, excluding this collector; not whole Windows or GPU/VRAM.','CommandToSlideMs measures first slide submission; DesktopReadyMs includes preparing and committing the live desktop. CachedPreview distinguishes the early-preview path.','Process memory includes native allocations; GCBytes alone is not total memory.','CacheBytes is retained CPU snapshot data; GPU textures and temporary capture buffers are additional.','First visit is a cache miss. Compare cold and warm behavior separately.')} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $folder 'environment.json') -Encoding UTF8
 $summaries | Format-Table -AutoSize
 $latencySummary | Format-Table -AutoSize
 Write-Host "Report saved to: $folder"
 Start-Process explorer.exe -ArgumentList ('"'+$folder+'"')
}finally{
 try{Set-MeasurementMode $original}catch{Write-Warning 'Restore the original capture mode in settings.'}
 $mutex.ReleaseMutex();$mutex.Dispose()
}
[void](Read-Host 'Press Enter to close')
