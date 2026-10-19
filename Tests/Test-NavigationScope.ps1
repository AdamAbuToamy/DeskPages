$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path (Split-Path $PSScriptRoot -Parent) 'Native/DeskPagesNavigationPolicy.cs')
if([DeskPagesNavigationPolicy]::AllowPreparation($false,$false,$false,$false)){throw 'Unscoped input allowed over applications'}
foreach($flags in @(@($true,$false,$false,$false),@($false,$true,$false,$false),@($false,$false,$true,$false),@($false,$false,$false,$true))){
 if(-not [DeskPagesNavigationPolicy]::AllowPreparation($flags[0],$flags[1],$flags[2],$flags[3])){throw 'Explicit panel/desktop/tray command was blocked'}
}
Write-Host 'PASS: explicit panel navigation can prepare desktop-only animation over apps; unscoped app input stays blocked.'
