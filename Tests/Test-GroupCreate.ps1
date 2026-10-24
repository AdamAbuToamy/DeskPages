$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$tokens=$null;$errors=$null
$tree=[Management.Automation.Language.Parser]::ParseFile((Join-Path $root 'Workspace.ps1'),[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Workspace did not parse.'}
$fn=$tree.Find({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Add-DpTaskbarGroup'},$true)
if(-not $fn){throw 'Group create command missing.'}
Invoke-Expression $fn.Extent.Text
$script:base=$root;$script:enabled=$true;$script:enables=0;$script:launches=@()
function Get-DpGroupsEnabled {return $script:enabled}
function Set-DpGroupsEnabled {param([bool]$Enabled)$script:enabled=$Enabled;$script:enables++}
$shell=New-Object PSObject
$shell | Add-Member ScriptMethod ShellExecute {param($file,$arguments,$directory,$verb,$show)$script:launches+=,@($file,$arguments,$directory,$verb,$show)}
$script:originalDesktop=[pscustomobject]@{Document=[pscustomobject]@{Application=$shell}}
Add-DpTaskbarGroup
if($script:enables -ne 0 -or $script:launches.Count -ne 1 -or $script:launches[0][1] -ne '--add'){throw 'Already-enabled group creation waited on its running mutex or lost the add request.'}
$script:enabled=$false
Add-DpTaskbarGroup
if($script:enables -ne 1 -or -not $script:enabled -or $script:launches.Count -ne 2 -or $script:launches[1][1] -ne '--add'){throw 'Disabled groups were not enabled and sent an add request.'}
Write-Host 'PASS: tray group creation sends --add to the normal-user Shell; existing enabled controller is not blocked by its lifetime mutex; disabled feature is enabled before creation.'
