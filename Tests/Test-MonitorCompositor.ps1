$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
$temp=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-cpp-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try{
 foreach($fixture in @('Test-MonitorPlan.cpp','Test-SnapshotCache.cpp','Test-SnapshotPipeline.cpp')){
 $source=Join-Path $base ('Native/Compositor/'+$fixture);$output=Join-Path $temp 'monitor-simulator.exe'
 if(Get-Command cl.exe -ErrorAction SilentlyContinue){Push-Location $temp;try{& cl.exe /nologo /std:c++17 /EHsc /O2 $source ('/Fe:'+ $output);if($LASTEXITCODE -ne 0){throw 'C++ monitor simulator compilation failed.'}}finally{Pop-Location}}
 elseif(Get-Command g++ -ErrorAction SilentlyContinue){& g++ -std=c++17 -O2 -Wall -Wextra $source -o $output;if($LASTEXITCODE -ne 0){throw 'C++ monitor simulator compilation failed.'}}
 else{Write-Host 'SKIP: monitor compositor math simulator needs a C++17 compiler (build/CI only).';return}
 & $output
 if($LASTEXITCODE -ne 0){throw 'Monitor compositor simulation failed.'}
 }
}finally{Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue}
