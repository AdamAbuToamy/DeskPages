$ErrorActionPreference='Stop'
if($env:OS -ne 'Windows_NT'){Write-Host 'SKIP: actual installer wordmark raster/font/DPI verification requires Windows.';return}
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$source=[IO.File]::ReadAllText((Join-Path (Split-Path $PSScriptRoot -Parent) 'Installer/SetupWindow.cs'))
$source='using System;using System.Drawing;using System.Windows.Forms;'+$source.Substring($source.IndexOf('internal sealed class SingleLineWordmark'))
$type=(Add-Type -TypeDefinition $source -ReferencedAssemblies @('System.Drawing','System.Windows.Forms') -PassThru | Where-Object {$_.Name -eq 'SingleLineWordmark'})
foreach($scale in @(1,1.25,1.5,2,3)){
 $control=[Activator]::CreateInstance($type,$true)
 $image=[Drawing.Bitmap]::new([int](182*$scale),[int](56*$scale))
 $image.SetResolution([single](96*$scale),[single](96*$scale))
 $graphics=[Drawing.Graphics]::FromImage($image)
 try{
  $control.Text='DeskPages';$control.Font=[Drawing.Font]::new('Segoe UI',25,[Drawing.FontStyle]::Bold)
  $control.Size=[Drawing.Size]::new($image.Width,$image.Height)
  $method=$type.GetMethod('FitFont',[Reflection.BindingFlags]'Instance,NonPublic')
  $font=$method.Invoke($control,[object[]]@($graphics))
  try{
   $flags=[Windows.Forms.TextFormatFlags]'SingleLine,NoPadding,NoPrefix'
   $size=[Windows.Forms.TextRenderer]::MeasureText($graphics,'DeskPages',$font,[Drawing.Size]::new([int]::MaxValue,[int]::MaxValue),$flags)
   if($size.Width -gt $control.Width -or $size.Height -gt $control.Height -or $font.SizeInPoints -lt 16){throw ('Clipped/unreadable wordmark at '+$scale)}
  }finally{$font.Dispose()}
  $control.DrawToBitmap($image,[Drawing.Rectangle]::new(0,0,$image.Width,$image.Height))
 }finally{$graphics.Dispose();$image.Dispose();$control.Font.Dispose();$control.Dispose()}
}
Write-Host 'PASS: single-line full wordmark fits actual Windows Segoe UI metrics at 100/125/150/200/300 percent DPI'
