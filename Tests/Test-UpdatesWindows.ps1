$ErrorActionPreference='Stop'
if($env:OS -ne 'Windows_NT'){Write-Host 'SKIP: .NET Framework update selector requires Windows.';return}
$root=Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName System.Web.Extensions
Add-Type -Path (Join-Path $root 'Bin/DeskPagesNative.dll')
function Release([string]$Version,[string]$Url='',[bool]$Draft=$false){
 $tag='v'+$Version+'-alpha.1';$base='https://github.com/AdamAbuToamy/DeskPages'
 if(-not $Url){$Url=$base+'/releases/download/'+$tag+'/DeskPages-Setup-'+$Version+'.exe'}
 @{tag_name=$tag;html_url=$base+'/releases/tag/'+$tag;draft=$Draft;assets=@(@{name='DeskPages-Setup-'+$Version+'.exe';state='uploaded';browser_download_url=$Url})}
}
$cases=@(
 @{Rows=@((Release '0.8.18'),(Release '0.8.29'),(Release '0.8.19'));Expected='0.8.29'},
 @{Rows=@((Release '0.8.17'),(Release '0.8.16'));Expected=''},
 @{Rows=@((Release '0.8.18' -Draft $true));Expected=''},
 @{Rows=@((Release '0.8.18' 'https://evil.invalid/update.exe'));Expected=''}
)
foreach($case in $cases){$json=ConvertTo-Json -InputObject @($case.Rows) -Depth 8 -Compress;$result=[DeskPagesUpdates]::Select($json,[Version]'0.8.17');if([string]$result.Version -ne $case.Expected){throw 'Unsafe or incorrect update selection.'}}
$bad=Release '0.8.18';$bad.assets=@();if([DeskPagesUpdates]::Select((ConvertTo-Json -InputObject @($bad) -Depth 8),[Version]'0.8.17')){throw 'Release without installer advertised.'}
Write-Host 'PASS: newest semantic release, current/older/draft rejection, official installer URL restriction and missing-asset rejection. No network or installer execution.'
