$ErrorActionPreference='Stop'
if($env:OS -ne 'Windows_NT'){Write-Host 'SKIP: actual compact panel layout and keyboard UI require Windows.';return}
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$root=Split-Path $PSScriptRoot -Parent
Add-Type -Path @((Join-Path $root 'Native/DeskPagesPanel.cs'),(Join-Path $root 'Native/DeskPagesTheme.cs'),(Join-Path $root 'Native/DeskPagesWorkspaceControls.cs')) -ReferencedAssemblies @('System.Drawing','System.Windows.Forms')
$names=[Func[int,string]]{param($id) if($id -eq 1){'الصفحة الرئيسية'}else{'Long project page name 0123456789012345'}}
$action=[Action[int]]{param($code)};$rename=[Action[int,string]]{param($id,$value)};$setting=[Action[int,bool]]{param($index,$value)};$document=[Action[string]]{param($name)}
foreach($scale in @(1,1.25,1.5,2)){
    $panel=[DeskPagesPanel]::new($action,$names,$rename,[bool[]]@($true,$false,$true,$true,$true,$true),$setting,$document)
    try{
        $panel.SetPages([int[]]@(1,2,3,8),3)
        $panel.CreateControl();$panel.Scale([Drawing.SizeF]::new($scale,$scale));$panel.PerformLayout()
        if($panel.ShowInTaskbar -or $panel.FormBorderStyle -ne [Windows.Forms.FormBorderStyle]::FixedToolWindow){throw 'Not a compact utility panel'}
        $tabs=@($panel.Controls | Where-Object {$_ -is [Windows.Forms.TabControl]})[0]
        if($tabs.TabPages.Count -ne 3){throw 'Missing panel section'}
        foreach($tab in $tabs.TabPages){$tabs.SelectedTab=$tab;$panel.PerformLayout();[Windows.Forms.Application]::DoEvents();if($tab.ClientSize.Width -le 0 -or $tab.ClientSize.Height -le 0){throw 'Invalid page size'}}
        $panel.SetPages([int[]]@(1,2),1)
        $image=[Drawing.Bitmap]::new($panel.Width,$panel.Height)
        try{$panel.DrawToBitmap($image,[Drawing.Rectangle]::new(0,0,$panel.Width,$panel.Height))}finally{$image.Dispose()}
    }finally{$panel.Dispose()}
}
Write-Host 'PASS: Windows panel construction, long/Arabic names, three tabs, page updates and raster rendering at 100/125/150/200 percent scale. Manual keyboard/monitor acceptance still required.'
