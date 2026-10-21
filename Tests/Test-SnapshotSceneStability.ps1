$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
Add-Type -Path (Join-Path $base 'Native/DeskPagesSnapshotFiles.cs')
$root=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-scene-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try{
 $project=Join-Path $root 'project';New-Item -ItemType Directory -Path $project | Out-Null
 $icon=Join-Path $root 'item.txt';[IO.File]::WriteAllText($icon,'icon data')
 $stamp=[DeskPagesSnapshotFiles]::Stamp($root)
 foreach($n in 1..100){[IO.File]::WriteAllText((Join-Path $project 'build.log'),('output '+$n));if([DeskPagesSnapshotFiles]::Stamp($root) -ne $stamp){throw 'Descendant project writes invalidated every page'}}
 [IO.Directory]::SetLastWriteTimeUtc($project,[DateTime]::UtcNow.AddHours(1))
 if([DeskPagesSnapshotFiles]::Stamp($root) -ne $stamp){throw 'A folder metadata touch invalidated images'}
 if([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT){$attributes=[IO.File]::GetAttributes($icon);[IO.File]::SetAttributes($icon,($attributes -bor [IO.FileAttributes]::Hidden -bor [IO.FileAttributes]::System));if([DeskPagesSnapshotFiles]::Stamp($root) -ne $stamp){throw 'Ordinary page visibility invalidated images'};[IO.File]::SetAttributes($icon,$attributes)}
 [IO.File]::WriteAllText($icon,'different icon data');if([DeskPagesSnapshotFiles]::Stamp($root) -eq $stamp){throw 'Real file changes were missed'}
 $stamp=[DeskPagesSnapshotFiles]::Stamp($root);Move-Item -LiteralPath $icon -Destination (Join-Path $root 'renamed.txt');if([DeskPagesSnapshotFiles]::Stamp($root) -eq $stamp){throw 'Rename was missed'}
 Write-Host 'PASS: 100 descendant build writes and folder metadata changes preserve the scene; actual desktop file edits/renames change it. Windows-only attribute check is conditional.'
}finally{Remove-Item -LiteralPath $root -Recurse -Force}
