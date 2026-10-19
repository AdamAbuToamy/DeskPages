. (Join-Path $PSScriptRoot 'PageSafety.ps1')
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-safety-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
try {
    $file=Join-Path $fixture 'keep.txt'
    [IO.File]::WriteAllText($file,'content must survive page deletion checks')
    $directory=Join-Path $fixture 'empty-folder'
    New-Item -ItemType Directory -Path $directory | Out-Null
    $items=@([PSCustomObject]@{Page=2;Path=$file},[PSCustomObject]@{Page=2;Path=$directory})
    if (@(Get-DpPageDeleteBlockers $items 2).Count -ne 2) {throw 'Safety test: nonempty page was not blocked.'}
    [IO.File]::SetAttributes($file,[IO.FileAttributes]::Hidden)
    if (@(Get-DpPageDeleteBlockers $items 2).Count -ne 2) {throw 'Safety test: hidden content was not protected.'}
    if (@(Get-DpPageDeleteBlockers $items 3).Count -ne 0) {throw 'Safety test: unrelated page was blocked.'}
    $missing=@([PSCustomObject]@{Page=2;Path=(Join-Path $fixture 'absent.txt')})
    if (@(Get-DpPageDeleteBlockers $missing 2).Count -ne 0) {throw 'Safety test: missing file treated as live.'}
    if ([IO.File]::ReadAllText($file) -ne 'content must survive page deletion checks') {throw 'Safety test changed content.'}
}
finally {Remove-Item -LiteralPath $fixture -Recurse -Force}
