$ErrorActionPreference='Stop'
if(-not ('DeskPagesSurfacePolicy' -as [type])){Add-Type -Path (Join-Path (Split-Path $PSScriptRoot -Parent) 'Native/DeskPagesSurfacePolicy.cs')}
foreach($name in @('SysListView32','SHELLDLL_DefView','Progman','WorkerW')){
 if(-not [DeskPagesSurfacePolicy]::IsDesktopSurface($name,'Progman',$true)){throw 'Original desktop rejected'}
}
foreach($popup in @('#32768','Xaml_WindowedPopupClass','Microsoft.UI.Content.DesktopChildSiteBridge','Edit','tooltips_class32','CabinetWClass')){
 # Even an owner falsely reported as the original desktop cannot admit a menu.
 if([DeskPagesSurfacePolicy]::IsDesktopSurface($popup,'Progman',$true)){throw ('Popup/rename editor accepted: '+$popup)}
}
if([DeskPagesSurfacePolicy]::IsDesktopSurface('SysListView32','CabinetWClass',$false)){throw 'Explorer folder window treated as desktop'}
Write-Host 'PASS: original desktop surfaces accepted; owned Win32/XAML menus, rename editors, tooltips and Explorer folder views rejected'
