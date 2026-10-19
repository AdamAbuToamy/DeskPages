param([switch]$Child)
$ErrorActionPreference='Stop'
if(-not $Child){$exe=[Diagnostics.Process]::GetCurrentProcess().MainModule.FileName;& $exe -NoProfile -File $PSCommandPath -Child;if($LASTEXITCODE -ne 0){throw 'Drag mailbox fixture failed'};return}
$root=Split-Path $PSScriptRoot -Parent
Add-Type -TypeDefinition ('using System.Diagnostics;'+[Environment]::NewLine+([IO.File]::ReadAllText((Join-Path $root 'Native/DeskPagesDragProtocol.cs')))+[Environment]::NewLine+([IO.File]::ReadAllText((Join-Path $root 'Native/DeskPagesDragMailbox.cs')).Replace('using System;','').Replace('using System.IO;','').Replace('using System.Diagnostics;','')))
$token=[guid]::NewGuid().ToString('N');$folder=[DeskPagesDragProtocol]::SessionPath($token)
$mailbox=[DeskPagesDragMailbox]::new($token)
function Send-Request {param($Kind)
 $r=[DeskPagesDragRequest]::new();$r.Id=[guid]::NewGuid().ToString('N');$r.Kind=$Kind;$r.X=1
 if($Kind -eq 'Arm'){$r.Paths=[string[]]@('C:\Desktop\fixture.txt')}
 [DeskPagesDragProtocol]::AtomicText((Join-Path $folder 'request.txt'),[DeskPagesDragProtocol]::Encode($r));return $r
}
try{
 # Heartbeat and reply read contention is also retryable, not missing/malformed data.
 foreach($name in @('state.txt','response.txt')){
  $path=Join-Path $folder $name
  if(-not [IO.File]::Exists($path)){[IO.File]::WriteAllText($path,'fixture')}
  $hold=[IO.FileStream]::new($path,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
  try{$lines=$null;if([DeskPagesDragProtocol]::TryReadLines($path,[ref]$lines)){throw 'Locked peer snapshot was reported as readable'}}finally{$hold.Dispose()}
  $lines=$null;if(-not [DeskPagesDragProtocol]::TryReadLines($path,[ref]$lines)){throw 'Peer snapshot remained blocked after unlock'}
 }
 Write-Host 'PASS: locked heartbeat/response reads defer without invalidating peer state and recover after unlock.'
 $visualPath=Join-Path $folder 'visual-state.txt'
 $mailbox.PublishVisualState($false)
 if(Test-Path -LiteralPath $visualPath){throw 'Idle mailbox invented navigation completion'}
 $file=Join-Path $folder 'request.txt';$arm=Send-Request Arm
 # Real exclusive file handle: read contention must not delete or consume the request.
 $hold=[IO.FileStream]::new($file,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
 try{
  $negative=$false;try{$null=[IO.File]::ReadAllText($file)}catch [IO.IOException]{$negative=$true}
  if(-not $negative){throw 'Runtime did not enforce exclusive sharing: contention test is invalid'}
  for($i=0;$i -lt 5;$i++){if($mailbox.Poll(1,$true,2)){throw 'Locked request dispatched'}}
  if(-not [IO.File]::Exists($file) -or $mailbox.Pending){throw 'Transient sharing failure destroyed/consumed request'}
 }finally{$hold.Dispose()}
 if(-not $mailbox.Poll(1,$true,2) -or $mailbox.Pending.Id -ne $arm.Id){throw 'Request lost after releasing lock'}
 $mailbox.Complete($true,1)
 if($mailbox.Poll(1,$true,2)){throw 'Arm replayed'}
 Write-Host 'PASS: actual exclusive request lock reproduced; request retained and accepted once after unlock.'
 # Fail an acknowledgement write after a successful command using a blocked destination.
 $reply=Join-Path $folder 'response.txt';Remove-Item -LiteralPath $reply;New-Item -ItemType Directory -Path $reply | Out-Null
 $nav=Send-Request Navigate
 if(-not $mailbox.Poll(1,$true,2)){throw 'Navigate not delivered'}
 $mailbox.Complete($true,2)
 $mailbox.PublishVisualState($true)
 $visual=$null;if(-not [DeskPagesDragProtocol]::TryReadLines($visualPath,[ref]$visual) -or $visual[1] -ne $nav.Id -or [DeskPagesDragProtocol]::NavigationSettled($visual,$nav.Id)){throw 'Command acknowledgement prematurely settled animation'}
 for($i=0;$i -lt 5;$i++){if($mailbox.Poll(2,$true,2)){throw 'Committed navigation dispatched twice while acknowledgement blocked'}}
 if($mailbox.Pending.Id -ne $nav.Id){throw 'Unacknowledged command lost'}
 Remove-Item -LiteralPath $reply -Recurse
 if($mailbox.Poll(2,$true,2)){throw 'Ack retry redispatched navigation'}
 $text=[IO.File]::ReadAllText($reply)
 if($text -ne ($nav.Id+"`nOK`n2") -or $mailbox.Pending){throw 'Exact acknowledgement not retried'}
 if($mailbox.Poll(2,$true,2)){throw 'Acknowledged navigation replayed'}
 $drop=Send-Request Drop;if(-not $mailbox.Poll(2,$true,2) -or $mailbox.Pending.Id -ne $drop.Id){throw 'Next request blocked by completed acknowledgement'};$mailbox.Complete($true,2)
 $mailbox.PublishVisualState($false)
 $visual=$null;$null=[DeskPagesDragProtocol]::TryReadLines($visualPath,[ref]$visual)
 if(-not [DeskPagesDragProtocol]::NavigationSettled($visual,$nav.Id) -or [DeskPagesDragProtocol]::NavigationSettled($visual,$drop.Id)){throw 'Renderer completion missing or applied to another request'}
 Write-Host 'PASS: failed acknowledgement retained/retried; successful navigation never dispatched twice; next drop accepted.'
 [DeskPagesDragProtocol]::AtomicText($file,'invalid-packet')
 $rejected=$false;try{$null=$mailbox.Poll(2,$true,2)}catch [ArgumentException]{$rejected=$true}
 if(-not $rejected -or -not [IO.File]::Exists($file) -or $mailbox.Poll(2,$true,2)){throw 'Malformed request handling deletes packets or repeats failures'}
 $cancel=Send-Request Cancel;if(-not $mailbox.Poll(2,$true,2)){throw 'Valid packet after malformed request was rejected'};$mailbox.Complete($true,2)
 Write-Host 'PASS: malformed packet rejected once without deleting a replacement-capable mailbox; valid follow-up accepted.'
 # A stale completion and blocked visual-state write must not retire a new cue.
 $nav2=Send-Request Navigate;if(-not $mailbox.Poll(2,$true,2)){throw 'Second navigation missing'};$mailbox.Complete($true,1)
 if([DeskPagesDragProtocol]::NavigationSettled($visual,$nav2.Id)){throw 'Stale completion retired new navigation cue'}
 Remove-Item -LiteralPath $visualPath;New-Item -ItemType Directory -Path $visualPath | Out-Null
 $mailbox.PublishVisualState($true);Remove-Item -LiteralPath $visualPath -Recurse
 $mailbox.PublishVisualState($true)
 $visual=$null;$null=[DeskPagesDragProtocol]::TryReadLines($visualPath,[ref]$visual)
 if($visual[1] -ne $nav2.Id -or $visual[2] -ne 'PLAYING'){throw 'Busy visual-state write was not retried'}
 $mailbox.PublishVisualState($false)
 $visual=$null;$null=[DeskPagesDragProtocol]::TryReadLines($visualPath,[ref]$visual)
 if(-not [DeskPagesDragProtocol]::NavigationSettled($visual,$nav2.Id)){throw 'New renderer completion not published'}
 Write-Host 'PASS: cue completion waits for renderer settlement; matching request ID required; stale completion rejected; blocked signal write retried.'
 # Competing atomic writes/read snapshots exercise real IO with UTF-8 payloads.
 $snapshot=Join-Path $folder 'snapshot.txt';$payload=('ملف — snapshot '*200)
 [DeskPagesDragProtocol]::AtomicText($snapshot,$payload)
 $ready=Join-Path $folder 'writer.ready';$go=Join-Path $folder 'writer.go'
 $job=Start-Job -ArgumentList $snapshot,$payload,(Join-Path $root 'Native/DeskPagesDragProtocol.cs'),$ready,$go -ScriptBlock {
  param($path,$text,$source,$ready,$go)
  Add-Type -Path $source;[IO.File]::WriteAllText($ready,'ready')
  $clock=[Diagnostics.Stopwatch]::StartNew()
  while(-not [IO.File]::Exists($go)){if($clock.ElapsedMilliseconds -gt 10000){throw 'Writer barrier timed out'};Start-Sleep -Milliseconds 10}
  for($i=0;$i -lt 500;$i++){if(-not [DeskPagesDragProtocol]::TryAtomicText($path,$text)){throw 'Shared snapshot reader blocked atomic writer'}}
 }
 $clock=[Diagnostics.Stopwatch]::StartNew()
 while(-not [IO.File]::Exists($ready)){if($clock.ElapsedMilliseconds -gt 10000){throw 'Writer did not become ready'};Start-Sleep -Milliseconds 10}
 [IO.File]::WriteAllText($go,'start');$reads=0
 for($i=0;$i -lt 3000;$i++){$value=$null;if([DeskPagesDragProtocol]::TryReadText($snapshot,[ref]$value)){if($value -ne $payload){throw 'Partial or corrupt atomic snapshot read'};$reads++}}
 $null=Wait-Job $job -Timeout 30
 if($job.State -ne 'Completed'){Stop-Job $job;throw 'Atomic writer did not complete'}
 $null=Receive-Job $job -ErrorAction Stop;Remove-Job $job
 if($reads -lt 1){throw 'No successful snapshot reads'}
 Write-Host ('PASS: concurrent process atomic replacement/shared snapshot reads; complete Unicode snapshots='+$reads)
 Write-Host 'Windows FileShare.Delete rename semantics and physical OLE drag still require Windows acceptance.'
}finally{$mailbox.Dispose();if(Test-Path -LiteralPath $folder){Remove-Item -LiteralPath $folder -Recurse -Force}}
