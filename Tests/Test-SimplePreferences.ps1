$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Add-Type -Path (Join-Path $root 'Native/DeskPagesSimplePreferences.cs')
$result=[DeskPagesSimplePreferences]::Build('ar',[bool[]]@($false,$true,$false,$true),$true,$false,$true,2)
if(($result -join '|') -cne 'ar|False|True|False|True|True|False|True|2'){throw 'Simple settings binding changed.'}
foreach($case in @(@{Language='xx';Basic=[bool[]]@($true,$true,$true,$true);Inbox=0},@{Language='en';Basic=[bool[]]@($true);Inbox=0},@{Language='en';Basic=[bool[]]@($true,$true,$true,$true);Inbox=100})){
 $failed=$false;try{$null=[DeskPagesSimplePreferences]::Build($case.Language,$case.Basic,$false,$true,$false,$case.Inbox)}catch{$failed=$true}
 if(-not $failed){throw 'Invalid simple preferences accepted.'}
}
Write-Host 'PASS: exact simple preference mapping and invalid language/toggle-count/destination rejection.'
