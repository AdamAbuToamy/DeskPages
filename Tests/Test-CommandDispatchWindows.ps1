$ErrorActionPreference='Stop'
if($env:OS -ne 'Windows_NT'){Write-Host 'SKIP: posted-message host dispatch requires Windows.';return}
$root=Split-Path $PSScriptRoot -Parent
$runtime=Join-Path $root 'Bin/DeskPagesNative.dll'
$temp=Join-Path ([IO.Path]::GetTempPath()) ('dp-command-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try{
 $exe=Join-Path $temp 'CommandsTest.exe';$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
 & $compiler /noconfig /nologo /target:exe /platform:x64 /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll "/r:$runtime" "/out:$exe" (Join-Path $PSScriptRoot 'CommandDispatchHarness.cs')
 if($LASTEXITCODE -ne 0){throw 'Command dispatch harness failed to compile.'}
 Copy-Item -LiteralPath $runtime -Destination (Join-Path $temp 'DeskPagesNative.dll')
 $info=[Diagnostics.ProcessStartInfo]::new($exe,('"'+$runtime+'"'));$info.UseShellExecute=$false;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
 $child=[Diagnostics.Process]::Start($info)
 try{
  $stdout=$child.StandardOutput.ReadToEndAsync();$stderr=$child.StandardError.ReadToEndAsync()
  if(-not $child.WaitForExit(60000)){$child.Kill();$child.WaitForExit();Write-Host ($stdout.GetAwaiter().GetResult());Write-Host ($stderr.GetAwaiter().GetResult());throw 'Isolated command fixture exceeded 60 seconds.'}
  Write-Host ($stdout.GetAwaiter().GetResult());Write-Host ($stderr.GetAwaiter().GetResult())
  if($child.ExitCode -ne 0){throw 'Command dispatch regression failed.'}
 }finally{$child.Dispose()}
}finally{Remove-Item -LiteralPath $temp -Recurse -Force}
