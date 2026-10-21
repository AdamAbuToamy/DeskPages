# Temporary synthetic transition only: no desktop files, membership or icon positions change.
$ErrorActionPreference='Stop'
$area=[Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$a=[Drawing.Bitmap]::new($area.Width,$area.Height)
$b=[Drawing.Bitmap]::new($area.Width,$area.Height)
$lines=[Collections.Generic.List[string]]::new()
$lines.Add('DeskPages 0.8.18 native compositor test: synthetic pictures, not desktop pages.')
$backdrop=$null;$front=$null
try {
    foreach($pair in @(@($a,[Drawing.Color]::SteelBlue),@($b,[Drawing.Color]::DarkSlateGray))) {
        $graphics=[Drawing.Graphics]::FromImage($pair[0])
        try {
            $graphics.Clear($pair[1])
            for($x=20;$x -lt $area.Width;$x+=100){$graphics.FillRectangle([Drawing.Brushes]::White,$x,40,50,50)}
        } finally {$graphics.Dispose()}
    }
    for($run=0;$run -lt 5;$run++) {
        if(-not [DeskPagesAnimation]::Begin($a,$area)){throw 'Renderer was not ready.'}
        [DeskPagesAnimation]::Play($b,$(if($run%2){-1}else{1}))
        $deadline=[DateTime]::UtcNow.AddSeconds(2)
        while([DeskPagesAnimation]::IsVisible -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 20}
        if([DeskPagesAnimation]::IsVisible){throw 'Animation did not finish within two seconds.'}
        $lines.Add(('Run '+($run+1)+': '+[DeskPagesAnimation]::Metrics))
        Start-Sleep -Milliseconds 100
    }
    # Window isolation fixture: an occluded HWND must return its own color,
    # not the other window covering it. No desktop files/layouts are changed.
    $backdrop=[Windows.Forms.Form]::new();$front=[Windows.Forms.Form]::new()
    foreach($form in @($backdrop,$front)){
        $form.FormBorderStyle=[Windows.Forms.FormBorderStyle]::None
        $form.StartPosition=[Windows.Forms.FormStartPosition]::Manual;$form.AutoScaleMode=[Windows.Forms.AutoScaleMode]::None
        $form.ShowInTaskbar=$false;$form.Bounds=[Drawing.Rectangle]::new($area.Left+100,$area.Top+100,300,220)
    }
    $backdrop.BackColor=[Drawing.Color]::FromArgb(23,57,111);$front.BackColor=[Drawing.Color]::FromArgb(235,41,132)
    $backdrop.Show();$front.Show();[Windows.Forms.Application]::DoEvents()
    $expected=[BitConverter]::ToUInt32([BitConverter]::GetBytes([int]$backdrop.BackColor.ToArgb()),0)
    $actual=[DeskPagesAnimation]::ProbeWindow($backdrop.Handle,$backdrop.Bounds)
    if($actual -ne $expected){throw 'Isolated HWND capture included occlusion or returned invalid content.'}
    $lines.Add('PASS: occluded source HWND renders its own pixels, excluding the covering test window.')
    Connect-DpDesktop
    $count=@([OriginalDesktopReader]::Read($script:originalDesktop)).Count
    $frontBounds=$front.Bounds
    if(-not [DeskPagesAnimation]::BeginDesktop($area)){throw ('Desktop isolation not available: '+[DeskPagesAnimation]::Metrics)}
    [DeskPagesAnimation]::ConfirmDesktopTarget($count)
    [DeskPagesAnimation]::CaptureAndPlay(1)
    $expectedFront=$front.BackColor.ToArgb() -band 0xFFFFFF
    $samples=0;$pixel=[Drawing.Bitmap]::new(1,1)
    try{
        $deadline=[DateTime]::UtcNow.AddSeconds(2)
        while([DeskPagesAnimation]::IsVisible -and [DateTime]::UtcNow -lt $deadline){
            [Windows.Forms.Application]::DoEvents()
            $graphics=[Drawing.Graphics]::FromImage($pixel)
            try{$graphics.CopyFromScreen($frontBounds.Left+150,$frontBounds.Top+110,0,0,[Drawing.Size]::new(1,1))}finally{$graphics.Dispose()}
            if(($pixel.GetPixel(0,0).ToArgb() -band 0xFFFFFF) -ne $expectedFront){throw 'Transition covered/slid the foreground test window.'}
            $samples++;Start-Sleep -Milliseconds 25
        }
    }finally{$pixel.Dispose()}
    if([DeskPagesAnimation]::IsVisible -or $front.Bounds -ne $frontBounds -or $samples -eq 0){throw 'Desktop-only transition did not complete with stationary foreground window.'}
    $lines.Add(('PASS: desktop-only transition kept the unrelated foreground test window stationary and unobscured; samples='+$samples))
    $lines.Add('Synthetic/isolation fixtures only. This is not a restart or physical display FPS certification.')
}
finally {
    [DeskPagesAnimation]::Shutdown()
    if($null -ne $front){$front.Close();$front.Dispose()}
    if($null -ne $backdrop){$backdrop.Close();$backdrop.Dispose()}
    $a.Dispose();$b.Dispose()
    $folder=Join-Path $env:LOCALAPPDATA 'DeskPages\Verification'
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    $report=Join-Path $folder 'animation-report.txt'
    $lines.ToArray() | Set-Content -LiteralPath $report -Encoding UTF8
    $lines.ToArray() | ForEach-Object {Write-Host $_}
    Start-Process notepad.exe -ArgumentList ('"'+$report+'"')
}
