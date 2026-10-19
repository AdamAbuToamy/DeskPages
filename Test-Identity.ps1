. (Join-Path $PSScriptRoot 'Layouts.ps1')

# Changed PIDL after hiding: restore the original coordinate using the current ID.
$saved=@([PSCustomObject]@{Name='code';Key='C:\Desktop\code';Id='old';X=17;Y=124})
$current=@([PSCustomObject]@{Name='code';Key='C:\Desktop\code';Id='new';X=500;Y=500})
$result=@(Resolve-DpLayoutTargets $saved $current)
if ($result.Count -ne 1 -or $result[0].Id -ne 'new' -or $result[0].X -ne 17 -or $result[0].Y -ne 124) {
    throw 'Identity test failed: changed PIDL.'
}
# Migration from the already-proven name-only snapshots.
$legacy=@([PSCustomObject]@{Name='code';X=31;Y=42})
$result=@(Resolve-DpLayoutTargets $legacy $current)
if ($result.Count -ne 1 -or $result[0].X -ne 31) {throw 'Identity test failed: legacy migration.'}
# Two same display names must not silently overwrite each other.
$duplicate=@(
    [PSCustomObject]@{Name='same';X=1;Y=1},
    [PSCustomObject]@{Name='same';X=2;Y=2})
$now=@([PSCustomObject]@{Name='same';Key='C:\Desktop\same';Id='x';X=9;Y=9})
if (@(Resolve-DpLayoutTargets $duplicate $now).Count -ne 0) {throw 'Identity test failed: ambiguous display names.'}
# Deleted items must not be repositioned; fresh items must keep their chosen position.
$now=@([PSCustomObject]@{Name='fresh';Key='C:\Desktop\fresh';Id='x';X=9;Y=9})
if (@(Resolve-DpLayoutTargets $saved $now).Count -ne 0) {throw 'Identity test failed: absent item.'}
# Stable path still works when the display name changes.
$now=@([PSCustomObject]@{Name='localized';Key='C:\Desktop\code';Id='z';X=9;Y=9})
$result=@(Resolve-DpLayoutTargets $saved $now)
if ($result.Count -ne 1 -or $result[0].Id -ne 'z') {throw 'Identity test failed: display-name change.'}
