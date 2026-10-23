function Copy-DpSecurityPayload {param([string]$PayloadRoot,[string]$PayloadInventory,[string]$PayloadStage)
 $ErrorActionPreference='Stop'
 $sourceRoot=[IO.Path]::GetFullPath($PayloadRoot)
 $entries=ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($PayloadInventory))
 if(-not $entries.Count){throw 'Empty build inventory.'}
 New-Item -ItemType Directory -Path $payloadStage | Out-Null
 $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
 foreach($entry in $entries){
  if($entry.Path -isnot [string] -or -not $entry.Path){throw 'Invalid build inventory path.'}
  $relative=[string]$entry.Path;$source=[IO.Path]::GetFullPath((Join-Path $sourceRoot $relative));$target=[IO.Path]::GetFullPath((Join-Path $payloadStage $relative))
  if([IO.Path]::IsPathRooted($relative) -or -not $source.StartsWith($sourceRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or -not $target.StartsWith($payloadStage+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or -not $seen.Add($relative)){throw 'Unsafe or duplicate build inventory entry.'}
  if((Get-Item -LiteralPath $source).Length -ne $entry.Length -or (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ine $entry.Sha256){throw ('Build inventory mismatch: '+$relative)}
  New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
  Copy-Item -LiteralPath $source -Destination $target
 }
 New-Item -ItemType Directory -Force (Join-Path $payloadStage 'Packaging') | Out-Null
 Copy-Item -LiteralPath $PayloadInventory -Destination (Join-Path $payloadStage 'Packaging/payload-inventory.json')
 return [PSCustomObject]@{Entries=$entries;Files=$entries.Count+1;Directory=$payloadStage}
}
