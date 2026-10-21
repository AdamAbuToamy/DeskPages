# Read-only release preflight. Validate bytes and metadata, not just filenames.
function Read-DpPortableExecutable {
    param([Parameter(Mandatory)][string]$Path,[int]$Machine=0,[switch]$Managed)
    $name=[IO.Path]::GetFileName($Path)
    if(-not [IO.File]::Exists($Path)){throw "Invalid DeskPages package: $name is missing."}
    $stream=[IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    $reader=[IO.BinaryReader]::new($stream)
    try{
        if($stream.Length -lt 4096){throw "Invalid DeskPages package: $name is empty or truncated. Download the corrected installer."}
        if($reader.ReadUInt16() -ne 0x5a4d){throw "Invalid DeskPages package: $name has no DOS signature."}
        $stream.Position=0x3c;$offset=$reader.ReadUInt32()
        if($offset -lt 64 -or $offset -gt $stream.Length-256){throw "Invalid DeskPages package: $name has an invalid PE offset."}
        $stream.Position=$offset
        if($reader.ReadUInt32() -ne 0x4550){throw "Invalid DeskPages package: $name has no PE signature."}
        $actualMachine=$reader.ReadUInt16();$sections=$reader.ReadUInt16()
        $stream.Position=$offset+20;$optionalSize=$reader.ReadUInt16()
        $stream.Position=$offset+24;$magic=$reader.ReadUInt16()
        if($sections -lt 1 -or $optionalSize -lt 224 -or $magic -notin @(0x10b,0x20b)){throw "Invalid DeskPages package: $name has invalid PE headers."}
        if($Machine -ne 0 -and $actualMachine -ne $Machine){throw "Invalid DeskPages package: $name has the wrong processor architecture."}
        $sectionStart=[long]$offset+24+$optionalSize
        if($sectionStart+40*[long]$sections -gt $stream.Length){throw "Invalid DeskPages package: $name has a truncated section table."}
        for($index=0;$index -lt $sections;$index++){
            $stream.Position=$sectionStart+40*$index+16
            $rawSize=$reader.ReadUInt32();$rawOffset=$reader.ReadUInt32()
            if([long]$rawOffset+$rawSize -gt $stream.Length){throw "Invalid DeskPages package: $name has a truncated PE section."}
        }
        if($Managed){
            $directoryStart=$offset+24+$(if($magic -eq 0x20b){112}else{96})
            if($directoryStart+15*8 -gt $sectionStart){throw "Invalid DeskPages package: $name has no CLR directory."}
            $stream.Position=$directoryStart+14*8
            if($reader.ReadUInt32() -eq 0 -or $reader.ReadUInt32() -lt 72){throw "Invalid DeskPages package: $name has no CLR metadata header."}
        }
        [PSCustomObject]@{Machine=$actualMachine;Magic=$magic;Length=$stream.Length}
    }finally{$reader.Dispose();$stream.Dispose()}
}
function Assert-DpManagedIdentity {
    param([string]$Path,[string]$Name,[Version]$Version)
    try{$identity=[Reflection.AssemblyName]::GetAssemblyName($Path)}
    catch{throw ('Invalid DeskPages package: '+[IO.Path]::GetFileName($Path)+' contains unreadable .NET assembly metadata. '+$_.Exception.Message)}
    if($identity.Name -ne $Name -or ($null -ne $Version -and $identity.Version -ne $Version)){
        throw ('Invalid DeskPages package: '+[IO.Path]::GetFileName($Path)+' assembly identity/version does not match this release.')
    }
}
function Assert-DpPackage {
    param([Parameter(Mandatory)][string]$Root,[Version]$Version)
    if($null -eq $Version){
        $text=[IO.File]::ReadAllText((Join-Path $Root 'VERSION.txt'))
        $match=[regex]::Match($text,'^\s*DeskPages\s+(\d+\.\d+\.\d+)\s*$')
        if(-not $match.Success){throw 'Invalid DeskPages package: release version is missing.'}
        $Version=[Version]($match.Groups[1].Value+'.0')
    }
    $guard=Join-Path $Root 'DeskPagesStartup.exe'
    $null=Read-DpPortableExecutable -Path $guard -Machine 0x8664 -Managed
    Assert-DpManagedIdentity -Path $guard -Name 'DeskPagesStartup' -Version $Version
    $drag=Join-Path $Root 'DeskPagesDrag.exe'
    $null=Read-DpPortableExecutable -Path $drag -Machine 0x8664 -Managed
    Assert-DpManagedIdentity -Path $drag -Name 'DeskPagesDrag' -Version $Version
    $rescue=Join-Path $Root 'DeskPagesRescue.exe'
    $null=Read-DpPortableExecutable -Path $rescue -Machine 0x8664 -Managed
    Assert-DpManagedIdentity -Path $rescue -Name 'DeskPagesRescue' -Version $Version
    $runtime=Join-Path $Root 'Bin/DeskPagesNative.dll'
    $null=Read-DpPortableExecutable -Path $runtime -Managed
    Assert-DpManagedIdentity -Path $runtime -Name 'DeskPagesNative'
    $null=Read-DpPortableExecutable -Path (Join-Path $Root 'DeskPages.exe') -Machine 0x8664
    $null=Read-DpPortableExecutable -Path (Join-Path $Root 'Native/DeskPagesCompositor.dll') -Machine 0x8664
    foreach($required in @('SnapshotCache.ps1','Native/DeskPagesSnapshotSession.cs','Native/DeskPagesSnapshotInvalidation.cs','Native/DeskPagesSnapshotScope.cs','Native/DeskPagesSnapshotFiles.cs','Native/Compositor/SnapshotDisk.h','Native/Compositor/SnapshotCache.h','Native/Compositor/PreviewGate.h','Tests/Test-ScriptEncoding.ps1','Native/DeskPagesScriptEncoding.cs','Native/DeskPagesTheme.cs','Native/DeskPagesWorkspaceControls.cs','Native/DeskPagesSimpleSettings.cs','Native/DeskPagesSimplePreferences.cs','Tests/Test-EdgeAnimationDirection.ps1','Tests/Test-PowerShell51.ps1','Native/DeskPagesWorkspaceRequest.cs','Workspace.ps1','WorkspaceCommands.ps1','Rescue.ps1','Agent.ps1','Install-Startup.ps1','FirstRun.ps1','Assets/DeskPages.ico','Bin/sources.sha256','EdgeDrag.ps1','ExportSettings.ps1','PRIVACY.txt','LICENSE.txt')){
        $path=Join-Path $Root $required
        if(-not [IO.File]::Exists($path) -or (Get-Item -LiteralPath $path).Length -eq 0){throw ('Invalid DeskPages package: missing or empty '+$required)}
    }
    $actual=(@(Get-ChildItem -LiteralPath (Join-Path $Root 'Native') -Filter '*.cs' -File | Sort-Object Name | ForEach-Object {(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash})) -join ':'
    if([IO.File]::ReadAllText((Join-Path $Root 'Bin/sources.sha256')).Trim() -ne $actual){throw 'Invalid DeskPages package: managed runtime source fingerprint does not match.'}
}
