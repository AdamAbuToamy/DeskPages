$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
$sources=(@('DeskPagesGesture.cs','DeskPagesDragProtocol.cs') | ForEach-Object {[regex]::Replace([IO.File]::ReadAllText((Join-Path $base ('Native/'+$_))),'(?m)^using [^;]+;\r?\n','')}) -join "`n"
$definition=@'
public static class DeskPagesInputPressure {
 public static string Run(){
  var gesture=new DeskPagesGesture();var edge=new DeskPagesEdgeDragPolicy();int emitted=0;
  for(int i=0;i<100000;i++){
   long time=i*2L;int result=gesture.Wheel(1,time,0);if(result!=0)emitted++;
   if(edge.Observe(time,100,0,1920,false)!=0)throw new Exception("No-drag navigation");
  }
  if(emitted!=1)throw new Exception("Inertia generated multiple page requests");
  gesture=new DeskPagesGesture();gesture.WheelThreshold=20;if(gesture.Wheel(19,1000,0)!=0 || gesture.Wheel(1,1010,0)!=1)throw new Exception("Calibrated wheel threshold");
  edge=new DeskPagesEdgeDragPolicy{ZoneWidth=80,HoldMilliseconds=200};
  if(edge.Observe(1000,75,0,1920,true)!=0 || edge.Observe(1199,75,0,1920,true)!=0 || edge.Observe(1200,75,0,1920,true)!=-1 || edge.Observe(2000,75,0,1920,true)!=0)throw new Exception("Calibrated edge or latch");
  var random=new Random(8031);int rejected=0;
  for(int i=0;i<10000;i++){
   var bytes=new byte[random.Next(1,200)];random.NextBytes(bytes);try{DeskPagesDragProtocol.Decode(Convert.ToBase64String(bytes));}catch(ArgumentException){rejected++;}
  }
  if(rejected!=10000)throw new Exception("Malformed drag packet accepted");
  return "PASS: 100,000 continuous wheel/edge samples, single inertia request, calibrated thresholds/latching and 10,000 malformed IPC packets rejected. Synthetic input only; no driver/device claim.";
 }
}
'@
Add-Type -TypeDefinition ("using System;using System.Collections.Generic;using System.IO;using System.Text;`n"+$sources+"`n"+$definition)
[DeskPagesInputPressure]::Run() | Write-Host
