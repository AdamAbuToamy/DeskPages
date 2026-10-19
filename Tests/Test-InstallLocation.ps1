param([switch]$Child)
if(-not $Child){
 $exe=[Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
 & $exe -NoProfile -File $PSCommandPath -Child
 if($LASTEXITCODE -ne 0){throw 'Install location tests failed.'}
 return
}
$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
Add-Type -Path (Join-Path $base 'Native/DeskPagesInstallLocation.cs')
foreach($rights in @('ReadAndExecute','Read','ListDirectory')){
 if([DeskPagesInstallLocation]::UnsafeRights([Security.AccessControl.FileSystemRights]$rights,$true)){throw ('Read right rejected '+$rights)}
}
foreach($rights in @('FullControl','Modify','Write','ChangePermissions','TakeOwnership','DeleteSubdirectoriesAndFiles','WriteAttributes')){
 if(-not [DeskPagesInstallLocation]::UnsafeRights([Security.AccessControl.FileSystemRights]$rights,$true)){throw ('Writable code root accepted '+$rights)}
}
if([DeskPagesInstallLocation]::UnsafeRights([Security.AccessControl.FileSystemRights]::CreateDirectories,$false)){throw 'Creating a sibling is not replacing an existing protected directory'}
if(-not [DeskPagesInstallLocation]::UnsafeRights([Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles,$false)){throw 'Replaceable ancestor accepted'}
Write-Host 'PASS: protected-install ACL rights policy; read/execute allowed, code replacement rights rejected'
if([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT){Write-Host 'SKIP: actual Windows drive/ACL/folder-picker/installer checks.';return}
foreach($bad in @([IO.Path]::GetPathRoot($env:ProgramFiles),$env:ProgramFiles,$env:USERPROFILE,$env:WINDIR,'\\server\share\DeskPages')){
 $failed=$false;try{[void][DeskPagesInstallLocation]::Resolve($bad)}catch{$failed=$true}
 if(-not $failed){throw ('Unsafe installation destination accepted: '+$bad)}
}
Write-Host 'PASS: real Windows path rejection cases'
