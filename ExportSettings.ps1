# Exports only explicitly allowed local metadata. Never reads desktop file contents.
function Export-DpSettings {
    param([Parameter(Mandatory=$true)][string]$Store,[Parameter(Mandatory=$true)][string]$Layouts,[Parameter(Mandatory=$true)][string]$Destination)
    $ErrorActionPreference='Stop'
    if(Test-Path -LiteralPath (Join-Path $Store 'transition.xml')){throw 'Finish the page transition before exporting.'}
    if(-not(Test-Path -LiteralPath (Join-Path $Store 'pages.xml') -PathType Leaf)){throw 'No saved pages to export.'}
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $staging=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-export-'+[guid]::NewGuid().ToString('N'))
    $output=[IO.Path]::GetFullPath($Destination)
    if(Test-Path -LiteralPath $output){throw 'Choose a new filename. Existing backups are never overwritten.'}
    $temporary=$output+'.'+[guid]::NewGuid().ToString('N')+'.tmp'
    New-Item -ItemType Directory -Path (Join-Path $staging 'PageLayouts') -Force | Out-Null
    try{
        foreach($file in @('pages.xml','input-settings.txt','page-names.txt')){
            $source=Join-Path $Store $file
            if(Test-Path -LiteralPath $source -PathType Leaf){
                if((Get-Item -LiteralPath $source).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Cannot export linked settings.'}
                Copy-Item -LiteralPath $source -Destination (Join-Path $staging $file)
            }
        }
        foreach($file in @(Get-ChildItem -LiteralPath $Layouts -Filter 'page-*.xml' -File -ErrorAction Stop)){
            if($file.Name -notmatch '^page-[1-9][0-9]?\.xml$'){continue}
            if($file.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Cannot export linked layouts.'}
            Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $staging 'PageLayouts')
        }
        [IO.File]::WriteAllText((Join-Path $staging 'README.txt'),"DeskPages metadata backup, UTC: $([DateTime]::UtcNow.ToString('o'))`r`nContains page membership, file paths, layout positions and preferences. No file contents.`r`nKeep private. This is not a backup of your files. Restore only with a matching account and verified recovery procedure; automatic import is intentionally unavailable.")
        [IO.Compression.ZipFile]::CreateFromDirectory($staging,$temporary)
        [IO.File]::Move($temporary,$output)
    }finally{
        Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
    }
}
