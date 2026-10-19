function New-DpInitialState {
    param([string]$Desktop,[string]$PublicDesktop,[object[]]$SystemBackup)
    $items=@(foreach($root in @($Desktop,$PublicDesktop)){
        foreach($file in Get-ChildItem -LiteralPath $root -Force -ErrorAction Stop){
            if(([int]$file.Attributes -band (2 -bor 4 -bor 1024)) -ne 0 -or $file.Name -ieq 'desktop.ini'){continue}
            [PSCustomObject]@{Path=$file.FullName;Page=1}
        }
    })
    [PSCustomObject]@{Desktop=$Desktop;Current=1;PageCount=2;Pages=@(1,2);Items=$items;SystemIconBackup=@($SystemBackup)}
}
function Get-DpInitialSystemBackup {
    $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($script:DpIconKey)
    try{
        foreach($id in $script:DpSystemIds){
            $exists=$null -ne $key -and $id -in @($key.GetValueNames())
            [PSCustomObject]@{Id=$id;Exists=$exists;Value=$(if($exists){$key.GetValue($id)}else{0});Kind=$(if($exists){$key.GetValueKind($id).ToString()}else{'DWord'})}
        }
    }finally{if($null -ne $key){$key.Dispose()}}
}
function Save-DpSafetySnapshot {
    $destination=Join-Path (Join-Path $env:LOCALAPPDATA 'DeskPages\SafetyBackups') ([DateTime]::UtcNow.Ticks.ToString())
    New-Item -ItemType Directory -Force $destination | Out-Null
    foreach($name in @('pages.xml','pages.xml.previous','transition.xml','suspended.xml')){
        $path=Join-Path $script:DpStore $name
        if(Test-Path -LiteralPath $path){Copy-Item -LiteralPath $path -Destination $destination}
    }
    $layouts=Join-Path $destination 'PageLayouts';New-Item -ItemType Directory -Force $layouts | Out-Null
    if(Test-Path -LiteralPath $script:DpLayoutStore){foreach($path in Get-ChildItem -LiteralPath $script:DpLayoutStore -File -Filter 'page-*.xml') {Copy-Item -LiteralPath $path.FullName -Destination $layouts}}
}
