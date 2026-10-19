$ErrorActionPreference='Stop'
if(-not ('DeskPagesVisibilitySync' -as [type])){Add-Type -Path (Join-Path (Split-Path $PSScriptRoot -Parent) 'Native/DeskPagesVisibilitySync.cs')}
$script:flags=[uint32]0x00101825;$original=$script:flags;$script:writes=0
$read=[Func[uint32]]{$script:flags}
$write=[Action[uint32,uint32]]{param($mask,$value) if($mask -ne 0x1000 -or $value -ne 0){throw 'Write mask changed unrelated settings'};$script:flags=($script:flags -band (-bnot $mask)) -bor ($value -band $mask);$script:writes++}
if(-not [DeskPagesVisibilitySync]::RevealIfStale($false,$read,$write)){throw 'Stale live NoIcons flag not repaired'}
if($script:flags -ne ($original -band (-bnot 0x1000)) -or $script:writes -ne 1){throw 'Arrange/grid/other folder flags changed'}
if([DeskPagesVisibilitySync]::RevealIfStale($false,$read,$write) -or $script:writes -ne 1){throw 'Already synchronized view rewritten'}
$script:flags=$original
if([DeskPagesVisibilitySync]::RevealIfStale($true,$read,$write) -or $script:flags -ne $original){throw 'User hidden-icons preference overridden'}
$bad=[Action[uint32,uint32]]{param($mask,$value) $script:flags=$script:flags -band (-bnot 0x1001)}
$rejected=$false;try{[DeskPagesVisibilitySync]::RevealIfStale($false,$read,$bad) | Out-Null}catch{$rejected=$true}
if(-not $rejected){throw 'Unverified change to arrange flags was accepted'}
Write-Host 'PASS: stale FWF_NOICONS cleared via single-bit mask; AutoArrange/grid/sort flags preserved; synchronized view untouched; user hide preference preserved; unexpected mutation rejected'
Write-Host 'Native Explorer flag calls/menu enablement were not executed by this adapter fixture.'
