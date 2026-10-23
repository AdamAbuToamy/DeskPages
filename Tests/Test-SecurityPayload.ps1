$ErrorActionPreference='Stop'
$helper=Join-Path (Split-Path $PSScriptRoot -Parent) 'Release/SecurityPayload.ps1'
if(-not(Test-Path -LiteralPath $helper)){Write-Host 'SKIP: build-time security staging fixture requires the source archive.';return}
. $helper
$temp=Join-Path ([IO.Path]::GetTempPath()) ('dp-security-test-'+[guid]::NewGuid().ToString('N'))
try{
 $root=Join-Path $temp 'source';New-Item -ItemType Directory -Force $root | Out-Null
 $entries=@(foreach($name in @('Agent.ps1','Native/runtime.dll','Assets/icon.png')){
  $path=Join-Path $root $name;New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null;[IO.File]::WriteAllText($path,('Fixture '+$name))
  [PSCustomObject]@{Path=$name;Length=(Get-Item -LiteralPath $path).Length;Sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}
 })
 $inventory=Join-Path $temp 'inventory.json';$entries | ConvertTo-Json | Set-Content $inventory -Encoding UTF8
 $copied=Copy-DpSecurityPayload $root $inventory (Join-Path $temp 'copied')
 if($copied.Entries.Count -ne 3 -or $copied.Files -ne 4){throw 'Inventory array was wrapped as one path.'}
 foreach($entry in $entries){if((Get-FileHash -LiteralPath (Join-Path $copied.Directory $entry.Path)).Hash -ine $entry.Sha256){throw 'Staged file differs from inventory.'}}
 $entries[0] | ConvertTo-Json | Set-Content $inventory -Encoding UTF8
 $one=Copy-DpSecurityPayload $root $inventory (Join-Path $temp 'single');if($one.Files -ne 2){throw 'Single-entry inventory rejected.'}
 foreach($case in @('hash','length','escape','duplicate')){
  $json=$entries | ConvertTo-Json;$bad=ConvertFrom-Json -InputObject $json
  if($case -eq 'hash'){$bad[0].Sha256='0000000000000000000000000000000000000000000000000000000000000000'}
  if($case -eq 'length'){$bad[0].Length=99999}
  if($case -eq 'escape'){$bad[0].Path='../outside.ps1'}
  if($case -eq 'duplicate'){$bad=@($entries[0],$entries[0])}
  $bad | ConvertTo-Json | Set-Content $inventory -Encoding UTF8
  $rejected=$false;try{Copy-DpSecurityPayload $root $inventory (Join-Path $temp $case) | Out-Null}catch{$rejected=$true}
  if(-not $rejected){throw ('Unsafe scan inventory accepted: '+$case)}
 }
 Write-Host 'PASS: production per-file scan staging handles JSON arrays and single entries, retains exact hashes and rejects altered hashes/lengths, path escape and duplicates. Antivirus itself is tested separately.'
}finally{Remove-Item -LiteralPath $temp -Recurse -Force}
