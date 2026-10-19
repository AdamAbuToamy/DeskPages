function Get-DpPageDeleteBlockers {
    param([object[]]$Items, [int]$Page)
    foreach ($entry in $Items) {
        if ([int]$entry.Page -ne $Page) {continue}
        try {
            $null=[IO.File]::GetAttributes([string]$entry.Path)
            $entry
        }
        catch [IO.FileNotFoundException] { }
        catch [IO.DirectoryNotFoundException] { }
        # Access errors are not evidence that a page is empty.
        catch {throw "Cannot verify whether this page is empty: $($entry.Path)"}
    }
}
