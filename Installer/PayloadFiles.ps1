# Build-time inventory: reviewed Git source plus explicitly named build outputs.
function Get-DpInstallerFiles {param([Parameter(Mandatory)][string]$Root)
    $Root=[IO.Path]::GetFullPath($Root)
    $tracked=@(& git -C $Root -c core.quotepath=false ls-files --cached)
    if($LASTEXITCODE -ne 0 -or -not $tracked.Count){throw 'Build the installer from a Git checkout; reviewed source inventory is unavailable.'}
    $generated=@('DeskPages.exe','DeskPagesStartup.exe','DeskPagesDrag.exe','DeskPagesRescue.exe','DeskPagesGroups.exe','Bin/DeskPagesNative.dll','Bin/sources.sha256','Native/DeskPagesCompositor.dll','Tests/DeskPagesSnapshotStoreTest.exe')
    $names=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($relative in @($tracked)+$generated){
        $relative=$relative.Replace('\','/')
        if($relative -match '^\.github/'){continue}
        if($relative -match '(^|/)(Seed|HiddenPages|PageLayouts|DragSessions|SafetyBackups|Backups|dist|\.git|Verification)(/|$)' -or $relative -match '(?i)(\.(pfx|p12|key|obj|pdb|tmp|previous|log)$|(^|/)installed-account\.txt$)'){throw ('Unwanted installer input: '+$relative)}
        if($relative -match '(?i)\.(exe|dll)$' -and $relative -notin $generated){throw ('Unlisted executable in source inventory: '+$relative)}
        $path=[IO.Path]::GetFullPath((Join-Path $Root $relative))
        if(-not $path.StartsWith($Root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Installer path escapes the source checkout.'}
        if(-not [IO.File]::Exists($path)){throw ('Installer input is missing: '+$relative)}
        if($names.Add($relative)){[PSCustomObject]@{Relative=$relative;Path=$path}}
    }
}
