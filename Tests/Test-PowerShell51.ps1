[CmdletBinding()]
param([switch]$RequireNative)
$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
Add-Type -Path (Join-Path $base 'Native/DeskPagesScriptEncoding.cs')
$encodingCount=[DeskPagesScriptEncoding]::ValidateDirectory($base)
Write-Host ("PASS: raw-byte encoding preflight accepted $encodingCount scripts.")
$native=$PSVersionTable.PSVersion.Major -eq 5 -and $PSVersionTable.PSVersion.Minor -eq 1
if($RequireNative -and -not $native){throw 'This release gate must run in Windows PowerShell 5.1, not PowerShell 7.'}
$count=0
foreach($file in Get-ChildItem -LiteralPath $base -Recurse -File -Filter '*.ps1'){
    $tokens=$null;$errors=$null
    $ast=[Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$errors)
    if($errors.Count){throw ($file.FullName+': '+(($errors | ForEach-Object {$_.Message}) -join '; '))}
    # Targeted cross-host guard, not a substitute for the native parser gate.
    $bad=@($ast.FindAll({param($node)
        ($node -is [Management.Automation.Language.TypeExpressionAst] -or $node -is [Management.Automation.Language.TypeConstraintAst]) -and
        $node.TypeName.FullName -match '^(System\.)?(Func|Action)\[.*\[\]'
    },$true))
    if($bad.Count){throw ('Array-valued generic delegate cast is forbidden in shipped PowerShell: '+$file.FullName)}
    $count++
}
if($native){Write-Host ("PASS: actual Windows PowerShell 5.1 parser accepted $count scripts.")}
else{Write-Host ("PASS: current-host parser and targeted legacy-delegate guard accepted $count scripts.");Write-Host 'SKIP: actual Windows PowerShell 5.1 parser is unavailable on this host.'}

# Exercise the exact production assignment without starting any Windows UI,
# touching Desktop state or relying on string conversions to a generic type.
$contract=[IO.File]::ReadAllText((Join-Path $base 'Native/DeskPagesWorkspaceRequest.cs'))
Add-Type -TypeDefinition ($contract+"`npublic static class DeskPagesCallbackProbe {public static DeskPagesWorkspaceRequest Request;}")
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $base 'Agent.ps1'),[ref]$tokens,[ref]$errors)
$assignment=@($ast.FindAll({param($node)
    $node -is [Management.Automation.Language.AssignmentStatementAst] -and
    $node.Left.Extent.Text -eq '[DeskPagesWorkspaceApi]::Request'
},$true))
if($assignment.Count -ne 1){throw 'Missing or ambiguous production workspace callback assignment.'}
$legacyType='[Func'+'[string,string'+ '[],string[]]]'
$legacy=$assignment[0].Extent.Text.Replace('[DeskPagesWorkspaceRequest]',$legacyType)
$legacyTokens=$null;$legacyErrors=$null
$legacyAst=[Management.Automation.Language.Parser]::ParseInput($legacy,[ref]$legacyTokens,[ref]$legacyErrors)
$legacyMatches=@($legacyAst.FindAll({param($node)
    ($node -is [Management.Automation.Language.TypeExpressionAst] -or $node -is [Management.Automation.Language.TypeConstraintAst]) -and
    $node.TypeName.FullName -match '^(System\.)?(Func|Action)\[.*\[\]'
},$true))
if(-not $legacyErrors.Count -and -not $legacyMatches.Count){throw 'The reported legacy cast escaped the compatibility gate.'}
Write-Host 'PASS: reported 0.8.0 callback syntax is rejected by the native parser or targeted cross-host guard.'
$script:received=$null
function Invoke-DpWorkspaceRequest {param([string]$Command,[string[]]$Arguments)
    $script:received=[PSCustomObject]@{Command=$Command;Arguments=@($Arguments)}
    if($Command -eq 'empty'){return}
    foreach($argument in $Arguments){$argument}
}
$code=$assignment[0].Extent.Text.Replace('[DeskPagesWorkspaceApi]','[DeskPagesCallbackProbe]')
& ([scriptblock]::Create($code))
$arabic=[string]::Concat([char[]]@(0x627,0x644,0x639,0x631,0x628,0x64a,0x629))
$hebrew=[string]::Concat([char[]]@(0x5e2,0x5d1,0x5e8,0x5d9,0x5ea))
$result=[DeskPagesCallbackProbe]::Request.Invoke('echo',[string[]]@($arabic,$hebrew,'C:\Desktop\file.txt'))
if(@($result).Count -ne 3 -or $result[0] -ne $arabic -or $result[1] -ne $hebrew -or $script:received.Command -ne 'echo' -or $script:received.Arguments.Count -ne 3){throw 'Production delegate changed array/Unicode arguments or results.'}
$result=[DeskPagesCallbackProbe]::Request.Invoke('empty',[string[]]@())
if($null -ne $result -and $result.Length -ne 0){throw 'Empty callback result was not empty.'}
Write-Host 'PASS: exact production named-delegate assignment, string-array arguments/results, Unicode and empty response binding; no desktop changes.'
