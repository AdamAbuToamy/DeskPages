$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
. (Join-Path $base 'Installer/PayloadFiles.ps1')
$temp=Join-Path ([IO.Path]::GetTempPath()) ('dp-payload-'+[guid]::NewGuid().ToString('N'))
try{
 New-Item -ItemType Directory -Force $temp | Out-Null
 & git -C $temp init --quiet
 [IO.File]::WriteAllText((Join-Path $temp 'Agent.ps1'),'reviewed source')
 New-Item -ItemType Directory -Force (Join-Path $temp '.github/workflows') | Out-Null
 [IO.File]::WriteAllText((Join-Path $temp '.github/workflows/build.yml'),'build: metadata')
 & git -C $temp add --all
 foreach($name in @('DeskPages.exe','DeskPagesStartup.exe','DeskPagesDrag.exe','DeskPagesRescue.exe','DeskPagesGroups.exe','Bin/DeskPagesNative.dll','Bin/sources.sha256','Native/DeskPagesCompositor.dll','Tests/DeskPagesSnapshotStoreTest.exe','Launcher.obj','unexpected.exe','unexpected.ps1')){
  $path=Join-Path $temp $name;New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null;[IO.File]::WriteAllText($path,'fixture bytes')
 }
 $files=@(Get-DpInstallerFiles $temp);$names=@($files.Relative)
 if($names.Count -ne 10 -or 'Agent.ps1' -notin $names -or 'DeskPages.exe' -notin $names){throw 'Reviewed source/runtime inputs missing.'}
 foreach($name in @('Launcher.obj','unexpected.exe','unexpected.ps1','.github/workflows/build.yml')){if($name -in $names){throw ('Unwanted file shipped: '+$name)}}
 Remove-Item -LiteralPath (Join-Path $temp 'DeskPagesDrag.exe')
 $blocked=$false;try{Get-DpInstallerFiles $temp | Out-Null}catch{$blocked=$true};if(-not $blocked){throw 'Missing runtime binary accepted.'}
 [IO.File]::WriteAllText((Join-Path $temp 'DeskPagesDrag.exe'),'fixture')
 & git -C $temp add unexpected.exe
 $blocked=$false;try{Get-DpInstallerFiles $temp | Out-Null}catch{$blocked=$true};if(-not $blocked){throw 'An unlisted tracked executable was accepted.'}
 Write-Host 'PASS: installer uses reviewed Git source and named runtime binaries; CI files, untracked scripts/executables and object files are excluded; missing runtime and unknown tracked executables block packaging.'
}finally{Remove-Item -LiteralPath $temp -Recurse -Force}
