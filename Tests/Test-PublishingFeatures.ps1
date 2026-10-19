$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Add-Type -Path (Join-Path $root 'Native/DeskPagesPageNames.cs')
. (Join-Path $root 'ExportSettings.ps1')
$temp=Join-Path ([IO.Path]::GetTempPath()) ('dp-publishing-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $temp 'store'),(Join-Path $temp 'layouts') -Force | Out-Null
try{
    $file=Join-Path $temp 'page-names.txt';$names=[DeskPagesPageNames]::new($file)
    $names.Rename(1,'الصفحة الرئيسية');$names.Rename(3,'Code : project')
    $loaded=[DeskPagesPageNames]::new($file)
    if($loaded.Get(1) -ne 'الصفحة الرئيسية' -or $loaded.Get(3) -ne 'Code : project'){throw 'Names did not round-trip'}
    foreach($bad in @('',('x'*41),"abc`nxyz")){$rejected=$false;try{$loaded.Rename(1,$bad)}catch{$rejected=$true};if(-not $rejected){throw 'Invalid name accepted'}}
    if([DeskPagesPageNames]::new($file).Get(1) -ne 'الصفحة الرئيسية'){throw 'Rejected input damaged saved names'}
    $store=Join-Path $temp 'store';$layouts=Join-Path $temp 'layouts';$out=Join-Path $temp 'backup.zip'
    [IO.File]::WriteAllText((Join-Path $store 'pages.xml'),'<test>metadata only</test>')
    [IO.File]::WriteAllText((Join-Path $store 'agent.log'),'private diagnostic log')
    [IO.File]::WriteAllText((Join-Path $layouts 'page-1.xml'),'<positions/>')
    [IO.File]::WriteAllText((Join-Path $layouts 'page-1.xml.archived'),'<old/>')
    Export-DpSettings $store $layouts $out
    $zip=[IO.Compression.ZipFile]::OpenRead($out)
    try{$entries=@($zip.Entries | ForEach-Object {$_.FullName});if($entries.Count -ne 3 -or 'pages.xml' -notin $entries -or 'PageLayouts/page-1.xml' -notin $entries -or 'README.txt' -notin $entries){throw 'Export allow-list failed'}}finally{$zip.Dispose()}
    $hash=(Get-FileHash $out).Hash;$blocked=$false;try{Export-DpSettings $store $layouts $out}catch{$blocked=$true};if(-not $blocked -or (Get-FileHash $out).Hash -ne $hash){throw 'Existing backup overwritten'}
    [IO.File]::WriteAllText((Join-Path $store 'transition.xml'),'pending');$blocked=$false;try{Export-DpSettings $store $layouts (Join-Path $temp 'pending.zip')}catch{$blocked=$true};if(-not $blocked -or (Test-Path (Join-Path $temp 'pending.zip'))){throw 'Uncommitted export accepted'}
    Write-Host 'PASS: Unicode page naming, validation, persistence, metadata-only export, no backup overwrite and transition export guard.'
}finally{Remove-Item -LiteralPath $temp -Recurse -Force}
