$ErrorActionPreference='Stop'
. (Join-Path (Split-Path $PSScriptRoot -Parent) 'Maintenance.ps1')
$root=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-upgrade-'+[Guid]::NewGuid().ToString('N'))
$store=Join-Path $root 'HiddenPages';$layouts=Join-Path $root 'PageLayouts'
New-Item -ItemType Directory -Force $store,$layouts | Out-Null
try {
 $state=[PSCustomObject]@{Current=3;Pages=@(1,2,3);Items=@([PSCustomObject]@{Path='preserved-code.txt';Page=3})}
 $state | Export-Clixml -LiteralPath (Join-Path $store 'pages.xml')
 [PSCustomObject]@{Page=1;Icons=@()} | Export-Clixml -LiteralPath (Join-Path $layouts 'page-1.xml')
 $blocked=$false;try{Copy-DpUpgradeCheckpoint -DataRoot $root}catch{$blocked=$true}
 if(-not $blocked){throw 'Checkpoint accepted without active-page layout'}
 [PSCustomObject]@{Page=3;Icons=@([PSCustomObject]@{Key='preserved-code.txt';X=17;Y=2})} | Export-Clixml -LiteralPath (Join-Path $layouts 'page-3.xml')
 $backup=Copy-DpUpgradeCheckpoint -DataRoot $root
 foreach($relative in @('HiddenPages/pages.xml','PageLayouts/page-1.xml','PageLayouts/page-3.xml')){
  if((Get-FileHash -LiteralPath (Join-Path $root $relative)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $backup $relative)).Hash){throw 'Backup bytes do not match'}
 }
 Write-Host 'PASS: upgrade fallback requires saved main/current layouts and preserves exact checkpoint bytes'
 $state.Current=4;$state.Pages=@(1,2,3,4);$state | Export-Clixml -LiteralPath (Join-Path $store 'pages.xml')
 $emptyBackup=Copy-DpUpgradeCheckpoint -DataRoot $root
 if(-not (Test-Path -LiteralPath $emptyBackup)){throw 'Empty new page incorrectly requires a missing layout'}
 Write-Host 'PASS: newly added empty current page can use preserved layouts of populated pages'
 Write-Host 'Process identity verification/termination is not executed by this fixture.'
}finally{Remove-Item -LiteralPath $root -Recurse -Force}
