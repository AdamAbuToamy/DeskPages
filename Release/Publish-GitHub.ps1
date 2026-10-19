# Creates the project's repository and dispatches a VALIDATED Windows build.
# It does not upload local AppData, credentials, binaries or personal desktop data.
param([string]$Repository='AdamAbuToamy/DeskPages')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if($Repository -notmatch '^[A-Za-z0-9-]+/[A-Za-z0-9_.-]+$'){throw 'Invalid repository name.'}
foreach($tool in @('git','gh')){if(-not(Get-Command $tool -ErrorAction SilentlyContinue)){throw "Install $tool first. GitHub CLI: https://cli.github.com/"}}
& gh auth status
if($LASTEXITCODE -ne 0){throw 'Run gh auth login first; no password or token is embedded in this package.'}
$owner=$Repository.Split('/')[0];$login=& gh api user --jq .login
if($LASTEXITCODE -ne 0 -or $login -ine $owner){throw 'Authenticate as the intended repository owner first.'}
if(-not(Test-Path -LiteralPath (Join-Path $root '.git'))){& git -C $root init -b main;if($LASTEXITCODE -ne 0){throw 'git init failed.'}}
& powershell.exe -NoProfile -File (Join-Path $root 'Tests/Test-PublicSource.ps1')
if($LASTEXITCODE -ne 0){throw 'Public-source preflight failed.'}
& git -C $root add --all
if($LASTEXITCODE -ne 0){throw 'git add failed.'}
$changes=& git -C $root diff --cached --name-only
if($changes){& git -C $root commit -m 'Release DeskPages 0.8.7 alpha: monitor animation and primary display scope';if($LASTEXITCODE -ne 0){throw 'Commit failed. Configure your git author identity first.'}}
$remotes=@(& git -C $root remote)
if('origin' -in $remotes){$remote=& git -C $root remote get-url origin;if($remote -notmatch ('[:/]'+[regex]::Escape($Repository)+'(?:\.git)?$')){throw 'Origin points to another repository. Nothing was pushed.'}}
else{
 & $env:ComSpec /d /c ('gh repo view '+$Repository+' --json name >nul 2>nul')
 if($LASTEXITCODE -eq 0){& git -C $root remote add origin ('https://github.com/'+$Repository+'.git')}
 else{& gh repo create $Repository --public --description 'Icon pages for the original Windows 11 desktop' --source $root --remote origin;if($LASTEXITCODE -ne 0){throw 'Repository creation failed.'}}
}
& git -C $root push -u origin main
if($LASTEXITCODE -ne 0){throw 'Push failed; no force push was attempted.'}
& gh api -X PUT ('repos/'+$Repository+'/private-vulnerability-reporting')
if($LASTEXITCODE -ne 0){throw 'Enable private vulnerability reporting in GitHub Settings > Security, then rerun. Release not dispatched.'}
& gh workflow run release.yml --repo $Repository --ref main -f publish=true
if($LASTEXITCODE -ne 0){throw 'Workflow dispatch failed. Open the repository Actions tab and run Validate and build Windows preview with publish enabled.'}
Write-Host ('Windows CI is building and testing. Watch: https://github.com/'+$Repository+'/actions')
Write-Host 'A public Pre-release appears only after build/validation succeeds. This command does not claim that CI has passed.'
