$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
Add-Type -Path (Join-Path $base 'Native/DeskPagesPageNames.cs')
Add-Type @'
public sealed class TestSpacing {public int X=80,Y=100;}
public static class DeskPagesLayout {public static TestSpacing Spacing(object value){return new TestSpacing();}}
'@
. (Join-Path $base 'StateEngine.ps1');. (Join-Path $base 'Workspace.ps1');. (Join-Path $base 'WorkspaceCommands.ps1');. (Join-Path $base 'NewItemPlacement.ps1')
function Assert($Pass,$Reason){if(-not $Pass){throw $Reason}}
function Get-DpMonitorGeometry {return $script:screens}
$temp=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-monitor-'+[guid]::NewGuid().ToString('N'))
try{
 $script:DpDesktop=Join-Path $temp 'Desktop';$script:DpPublic=Join-Path $temp 'Public';$script:DpStore=Join-Path $temp 'Store';$script:DpManifest=Join-Path $script:DpStore 'pages.xml';$script:DpLayoutStore=Join-Path $temp 'Layouts'
 New-Item -ItemType Directory -Force $script:DpDesktop,$script:DpPublic,$script:DpStore,$script:DpLayoutStore | Out-Null
 $a=Join-Path $script:DpDesktop 'primary.txt';$b=Join-Path $script:DpDesktop 'secondary.txt';$c=Join-Path $script:DpPublic 'other-page.txt'
 foreach($file in @($a,$b,$c)){[IO.File]::WriteAllText($file,'payload must never move or change')}
 $hashes=@{};foreach($file in @($a,$b,$c)){$hashes[$file]=(Get-FileHash $file).Hash}
 $script:DpState=[PSCustomObject]@{Desktop=$script:DpDesktop;Current=1;Pages=@(1,2,3);PageCount=3;Items=@([PSCustomObject]@{Path=$a;Page=1},[PSCustomObject]@{Path=$b;Page=1},[PSCustomObject]@{Path=$c;Page=2});SystemIconBackup=@()}
 Initialize-DpWorkspace
 $script:DpState.Workspace.PrimaryOnly=$true
 $count=0
 foreach($scale in @(1,1.25,1.5,2,2.5)){
  foreach($direction in @(-1,1)){
   $w=[int](1920*$scale);$h=[int](1080*$scale);$left=0;if($direction -lt 0){$left=-$w}
   $script:screens=@([PSCustomObject]@{Device='main';Left=0;Top=0;Right=$w;Bottom=$h;Primary=$true},[PSCustomObject]@{Device='second';Left=($direction*$w);Top=-200;Right=($direction*$w+$w);Bottom=($h-200);Primary=$false});$script:DpScopeScreens=$script:screens
   $icons=@([PSCustomObject]@{Key=$a;Name='primary';X=17;Y=2},[PSCustomObject]@{Key=$b;Name='secondary';X=($direction*$w+17);Y=2})
   $script:DpState.Workspace.SecondaryIcons=@(Get-DpSecondaryIcons $icons $script:screens)
   Assert (@($script:DpState.Workspace.SecondaryIcons).Count -eq 1) 'Secondary selection failed.'
   foreach($page in @(1,2,3,2,1)){
    Assert (Test-DpItemVisible $script:DpState.Items[1] $page) 'Secondary visibility changed with page.'
    Assert ((Test-DpItemVisible $script:DpState.Items[0] $page) -eq ($page -eq 1)) 'Primary membership wrong.'
    $saved=@([PSCustomObject]@{Key=$b;Name='secondary';X=1;Y=100},[PSCustomObject]@{Key=$c;Name='other';X=($direction*$w+100);Y=102})
    $targets=@(Get-DpScopedLayoutIcons $saved @());$stationary=@($targets | Where-Object Key -eq $b)[0];$paged=@($targets | Where-Object Key -eq $c)[0]
    Assert ($stationary.X -eq $icons[1].X -and $stationary.Y -eq 2) 'Page-specific layout moved secondary icon.'
    Assert ($paged.X -ge 0 -and $paged.X -lt $w) 'Page icon was not fitted onto Primary.'
    $script:DpState.Current=$page;Save-DpState;$script:DpState=Read-DpManifest $script:DpManifest
    Assert (Test-DpItemVisible $script:DpState.Items[1] $page) 'Reload lost display scope.';$count++
   }
   $script:DpScopeScreens=@($script:screens[0]);Assert (-not(Test-DpSecondaryKey $b)) 'Unplugged display left shared icons outside screen.'
   $targets=@(Get-DpScopedLayoutIcons @([PSCustomObject]@{Key=$b;Name='secondary';X=($direction*$w+17);Y=2}) @());$target=@($targets | Where-Object Key -eq $b)[0]
   # The adapter reports the unplugged geometry just as Windows would.
   $previous=$script:screens;$script:screens=$script:DpScopeScreens
   $targets=@(Get-DpScopedLayoutIcons @([PSCustomObject]@{Key=$b;Name='secondary';X=($direction*$w+17);Y=2}) @());$target=@($targets | Where-Object Key -eq $b)[0]
   Assert ($target.X -ge 0 -and $target.X -lt $w) 'Disconnected icon was not reflowed.'
   $script:screens=$previous;$script:DpScopeScreens=$previous;Assert (Test-DpSecondaryKey $b) 'Replug lost stationary membership.'
  }
 }
 $key1=Get-DpMonitorKey;$script:screens[0].Primary=$false;$script:screens[1].Primary=$true;$key2=Get-DpMonitorKey;Assert ($key1 -ne $key2) 'Changing the primary display did not select a distinct profile.';$script:screens[0].Primary=$true;$script:screens[1].Primary=$false
 $previousIcons=@($script:DpState.Workspace.SecondaryIcons);$second=$script:screens[1]
 $script:DpState.Workspace.SecondaryIcons=@([PSCustomObject]@{Key='::{26EE0668-A00A-44D7-9371-BEB064C98683}';Name='Control Panel';X=($second.Left+17);Y=2})
 Assert (Test-DpSecondarySystem '{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}') 'Secondary Control Panel alias was not retained.';$script:DpState.Workspace.SecondaryIcons=$previousIcons
 $before=(@($script:DpState.Items | ForEach-Object {$_.Path+'|'+$_.Page}) -join ';')
 $script:DpState.Workspace.PrimaryOnly=$false;Assert (-not(Test-DpItemVisible $script:DpState.Items[1] 2)) 'Disabling scope changed permanent pins.'
 foreach($file in @($a,$b,$c)){Assert ((Test-Path $file) -and (Get-FileHash $file).Hash -eq $hashes[$file]) 'File content/path changed.'}
 Assert ($before -eq (@($script:DpState.Items | ForEach-Object {$_.Path+'|'+$_.Page}) -join ';')) 'Scope changed page ownership.'
 Write-Host "PASS: $count primary-only page/reload scenarios, left/right monitors, negative origins, 5 pixel scales, secondary coordinates, unplug/replug, unchanged real file bytes and membership. Explorer adapter simulated."
}finally{Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue}
