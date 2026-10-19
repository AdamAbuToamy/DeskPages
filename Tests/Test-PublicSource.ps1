$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$forbidden=@('Seed','HiddenPages','PageLayouts','DragSessions','SafetyBackups','Backups','.env')
foreach($name in $forbidden){if(Test-Path -LiteralPath (Join-Path $root $name)){throw ('Personal state must not be published: '+$name)}}
foreach($file in Get-ChildItem -LiteralPath $root -Recurse -File -Force){
 $relative=$file.FullName.Substring($root.Length).TrimStart('\','/')
 if($relative -match '^\.git[\\/]|^dist[\\/]'){continue}
 if($file.Extension -in @('.pfx','.p12','.key') -or $file.Name -eq 'installed-account.txt' -or $file.Name -match '^firebase-adminsdk.*\.json$'){throw ('Private material in source tree: '+$relative)}
 if($file.Extension -in @('.cs','.ps1','.md','.txt','.json','.yml','.yaml')){
  $text=[IO.File]::ReadAllText($file.FullName)
  if($text -match '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----[\r\n]' -or $text -match '"type"\s*:\s*"service_account"'){throw ('Potential credentials in source: '+$relative)}
 }
}
Write-Host 'PASS: source preflight found no runtime page state, signing keys or service-account credentials. Review any later additions before committing.'
