$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Add-Type -Path (Join-Path $root 'Native/DeskPagesDragStartupPolicy.cs')
Add-Type @'
public static class DeskPagesDragStartupBindingProbe {
 public static string Value;
 public static bool CoreReady;
 public static void ConfigureDragSession(string value){Value=value;}
}
'@
# Reproduce the actual PowerShell -> C# string binding that caused 0.7.0 to fail.
[DeskPagesDragStartupBindingProbe]::ConfigureDragSession($null)
if($null -ne [DeskPagesDragStartupBindingProbe]::Value -and [DeskPagesDragStartupBindingProbe]::Value -ne ''){throw 'Unexpected string binding'}
if($null -ne [DeskPagesDragStartupPolicy]::NormalizeToken([DeskPagesDragStartupBindingProbe]::Value)){throw 'Bound null did not disable optional helper'}
foreach($invalid in @(''," `t",'not-a-token','../outside',[guid]::NewGuid().ToString('D'))){if($null -ne [DeskPagesDragStartupPolicy]::NormalizeToken($invalid)){throw 'Invalid token accepted'}}
$token=[guid]::NewGuid().ToString('N');if([DeskPagesDragStartupPolicy]::NormalizeToken($token) -ne $token){throw 'Valid session token rejected'}
# Run the production startup block with a mocked original Explorer object.
$source=[IO.File]::ReadAllText((Join-Path $root 'Agent.ps1'))
$start=$source.IndexOf('    try{'+[Environment]::NewLine+'        $dragToken=')
if($start -lt 0){$start=$source.IndexOf("    try{`n        `$dragToken=")}
$end=$source.IndexOf('    [DeskPagesHost]::Run($action)',$start)
$block=$source.Substring($start,$end-$start).Replace('[DeskPagesHost]','[DeskPagesDragStartupBindingProbe]')
$fake=Join-Path ([IO.Path]::GetTempPath()) ('dp-startup-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fake | Out-Null
try{
 [IO.File]::WriteAllText((Join-Path $fake 'DeskPagesDrag.exe'),'fixture only')
 $script:base=$fake;$script:logged=@()
 function Write-DpLog {param($Message) $script:logged+=@($Message)}
 foreach($mode in @('Supported','ShellFailure')){
  $application=[PSCustomObject]@{Fail=($mode -eq 'ShellFailure');Called=$false}
  $application | Add-Member -MemberType ScriptMethod -Name ShellExecute -Value {param($file,$arguments,$directory,$verb,$show) $this.Called=$true;if($this.Fail){throw 'fixture shell launch failure'}}
  # .Application intentionally has no ShellExecute: this recreates the device's object hierarchy.
  $script:originalDesktop=[PSCustomObject]@{Application=[PSCustomObject]@{};Document=[PSCustomObject]@{Application=$application}}
  [DeskPagesDragStartupBindingProbe]::Value='old-token'
  & ([scriptblock]::Create($block))
  if(-not $application.Called){throw 'Launch used the wrong COM object'}
  if($mode -eq 'Supported'){
   if($null -eq [DeskPagesDragStartupPolicy]::NormalizeToken([DeskPagesDragStartupBindingProbe]::Value)){throw 'Successful launch did not enable a valid session'}
  }else{
   if($null -ne [DeskPagesDragStartupPolicy]::NormalizeToken([DeskPagesDragStartupBindingProbe]::Value) -or -not $script:logged.Count){throw 'Shell failure was not isolated'}
  }
 }
 Write-Host 'PASS: actual PowerShell null-to-string binding, invalid token disabling, correct Document.Application launch and shell launch failure isolation.'
 Write-Host 'Mocked COM launch; physical Explorer launch/installer readiness still needs Windows.'
}finally{Remove-Item -LiteralPath $fake -Recurse -Force}
