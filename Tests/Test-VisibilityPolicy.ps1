$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
. (Join-Path $base 'StateEngine.ps1')
. (Join-Path $base 'Visibility.ps1')
$root=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-visibility-policy-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $root | Out-Null
try{
    $script:DpStore=$root;$script:DpDesktop=$root;$script:DpPublic=Join-Path $root 'Public'
    $path=Join-Path $root 'sentinel.txt';[IO.File]::WriteAllText($path,'unchanged')
    $hash=(Get-FileHash -LiteralPath $path).Hash
    foreach($case in @(
        @(128,$true,6),@(1,$true,7),@(32,$true,38),@(16,$true,22),
        @(6,$false,4),@(7,$false,5),@(2,$false,128)
    )){if((Get-DpVisibilityAttributes $path $case[0] $case[1]) -ne $case[2]){throw ('Unowned attribute policy failed: '+($case -join ','))}}
    Register-DpSystemOwnership -Paths @($path)
    $script:DpSystemOwnership=$null
    if(-not (Get-DpSystemOwnership).Contains($path)){throw 'Ownership did not survive a cache reload.'}
    foreach($case in @(@(6,$false,128),@(7,$false,1),@(38,$false,32),@(22,$false,16),@(128,$true,6))){
        if((Get-DpVisibilityAttributes $path $case[0] $case[1]) -ne $case[2]){throw ('Owned attribute policy failed: '+($case -join ','))}
    }
    if((Get-FileHash -LiteralPath $path).Hash -ne $hash){throw 'Policy/journal changed user bytes.'}
    Write-Host 'PASS: production Hidden/System policy preserves unowned System and unrelated bits; ownership is durable before a file attribute write.'
    Write-Host 'This portable policy fixture does not exercise Windows attributes or Explorer rendering.'
}finally{Remove-Item -LiteralPath $root -Recurse -Force}
