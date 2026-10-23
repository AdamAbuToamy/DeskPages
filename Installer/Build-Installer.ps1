param([string]$OutputDirectory,[string]$CertificateThumbprint,[switch]$RequireSignature)
$ErrorActionPreference='Stop'
if($RequireSignature -and -not $CertificateThumbprint){throw 'Public release requires a trusted code-signing identity. Supply CertificateThumbprint; no unsigned public release was built.'}
$root=Split-Path $PSScriptRoot -Parent
if(-not $OutputDirectory){$OutputDirectory=Join-Path $root 'dist'}
$work=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-build-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $work,$OutputDirectory | Out-Null
try {
    & (Join-Path $PSHOME 'powershell.exe') -NoProfile -STA -ExecutionPolicy Bypass -File (Join-Path $root 'Tests\Test-PowerShell51.ps1') -RequireNative
    if($LASTEXITCODE -ne 0){throw 'Windows PowerShell 5.1 parser/delegate gate failed.'}
    # Windows build machine only; users of Setup do not need a build environment.
    & (Join-Path $PSHOME 'powershell.exe') -NoProfile -STA -ExecutionPolicy Bypass -File (Join-Path $root 'Agent.ps1') -BuildNative
    if($LASTEXITCODE -ne 0){throw 'Managed runtime build/tests failed.'}
    & (Join-Path $root 'Guard\Build.ps1')
    & (Join-Path $root 'Interaction\Build.ps1')
    & (Join-Path $root 'Interaction\Build-Rescue.ps1')
    & (Join-Path $root 'Interaction\Build-Groups.ps1')
    & (Join-Path $PSHOME 'powershell.exe') -NoProfile -STA -ExecutionPolicy Bypass -File (Join-Path $root 'Tests\Run-Validation.ps1')
    if($LASTEXITCODE -ne 0){throw 'Workspace/regression release validation failed.'}
    $cert=$null
    if($CertificateThumbprint){
        $cert=Get-Item -LiteralPath ('Cert:\CurrentUser\My\'+$CertificateThumbprint)
        if(-not $cert.HasPrivateKey -or $cert.NotAfter -lt (Get-Date) -or '1.3.6.1.5.5.7.3.3' -notin @($cert.Extensions | Where-Object {$_.Oid.Value -eq '2.5.29.37'} | ForEach-Object {$_.EnhancedKeyUsages | ForEach-Object {$_.Value}})){throw 'A valid code-signing certificate with its private key is required.'}
        $chain=[Security.Cryptography.X509Certificates.X509Chain]::new()
        try{if(-not $chain.Build($cert)){throw 'Certificate chain is not trusted. Self-signed certificates do not establish a public publisher identity.'}}finally{$chain.Dispose()}
    }
    function Sign-ReleaseFile([string]$Path){
        if($cert){
            $result=Set-AuthenticodeSignature -LiteralPath $Path -Certificate $cert -HashAlgorithm SHA256 -TimestampServer 'http://timestamp.digicert.com'
            if($result.Status -ne 'Valid' -or -not $result.TimeStamperCertificate){throw ('Signing failed: '+$Path+' '+$result.StatusMessage)}
        }
    }
    foreach($file in Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object {$_.Extension -in @('.exe','.dll') -and $_.FullName -notlike ($OutputDirectory+'\*')}){Sign-ReleaseFile $file.FullName}
    . (Join-Path $root 'PackageIntegrity.ps1')
    Assert-DpPackage -Root $root -Version ([Version]'0.8.32.0')
    . (Join-Path $PSScriptRoot 'PayloadFiles.ps1')
    $packageFiles=@(Get-DpInstallerFiles $root)
    $inventory=@($packageFiles | ForEach-Object {[PSCustomObject]@{Path=$_.Relative;Length=(Get-Item -LiteralPath $_.Path).Length;Sha256=(Get-FileHash -LiteralPath $_.Path -Algorithm SHA256).Hash.ToLowerInvariant()}})
    $inventoryPath=Join-Path $work 'payload-inventory.json'
    [IO.File]::WriteAllText($inventoryPath,($inventory | ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
    $payload=Join-Path $work 'payload.zip'
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive=[IO.Compression.ZipFile]::Open($payload,[IO.Compression.ZipArchiveMode]::Create)
    try{
        foreach($file in $packageFiles){
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$file.Path,$file.Relative,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$inventoryPath,'Packaging/payload-inventory.json',[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }finally{$archive.Dispose()}
    $identity=Join-Path $work 'PayloadIdentity.cs'
    [IO.File]::WriteAllText($identity,('internal static class PayloadIdentity {internal const string Hash="'+(Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash+'";}'))
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    $provider=[Microsoft.CSharp.CSharpCodeProvider]::new()
    $parameters=[CodeDom.Compiler.CompilerParameters]::new()
    $parameters.GenerateExecutable=$true;$parameters.GenerateInMemory=$false
    $parameters.OutputAssembly=Join-Path $OutputDirectory 'DeskPages-Setup.exe'
    $parameters.CompilerOptions='/target:winexe /platform:x64 /optimize+ /win32icon:"'+(Join-Path $root 'Assets\DeskPages.ico')+'" /win32manifest:"'+(Join-Path $root 'Packaging\DeskPages.manifest')+'" /resource:"'+$payload+'",DeskPages.Payload'
    foreach($reference in @('System.dll','System.Core.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll',[Windows.Forms.Form].Assembly.Location,[Drawing.Bitmap].Assembly.Location)){$null=$parameters.ReferencedAssemblies.Add($reference)}
    try{
        $result=$provider.CompileAssemblyFromFile($parameters,[string[]]@((Join-Path $PSScriptRoot 'Setup.cs'),(Join-Path $PSScriptRoot 'SetupWindow.cs'),(Join-Path $root 'Native\DeskPagesInstallLocation.cs'),(Join-Path $root 'Native\DeskPagesScriptEncoding.cs'),$identity))
        if($result.Errors.HasErrors){throw (($result.Errors | ForEach-Object {$_.ToString()}) -join "`n")}
    }finally{$provider.Dispose()}
    Sign-ReleaseFile $parameters.OutputAssembly
    if(-not $cert){Write-Warning 'Unsigned release. Unknown Publisher / SmartScreen warnings may appear. No security settings were modified.'}
    if($RequireSignature){& (Join-Path $root 'Release\Verify-Release.ps1') -Installer $parameters.OutputAssembly -ExpectedThumbprint $CertificateThumbprint}
    Write-Host ('Built: '+$parameters.OutputAssembly)
}finally{Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue}
