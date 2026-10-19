# Build and publish

Users download only DeskPages-Setup-0.8.7.exe. Source/build tools are not required.

Developers: Windows x64, Windows PowerShell 5.1, .NET Framework 4.8, Visual Studio
Build Tools with Desktop development with C++, Windows SDK and C# compiler.

    powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File Release\Build-Windows.ps1

The wrapper finds the x64 toolchain, builds the launcher and DWM compositor,
then builds and validates managed components and the installer. Output: dist/.
A trusted Authenticode certificate is optional for this explicitly unsigned Alpha;
see SIGNING.md for signed builds. Never install a self-signed root to hide warnings.

Run Validate.cmd for automated logic/file/crash tests. With one physical display,
Simulate-Monitors.cmd exercises the actual DWM renderer using synthetic two-screen
images inside one window; it changes no desktop files or display configuration.
Its interactive Windows/GPU requirement is distinct from the portable math test.

Source builds skip GPU initialization; production installer readiness and explicit
self-tests still check the real renderer. The GitHub workflow builds on Windows Server and runs the automated stress suite; it
retains reports and assets. A normal push/PR does not publish a release. A manual
workflow dispatch with publish=true publishes only after its build job succeeds.
The workflow never claims that hosted Windows CI emulates physical touchpad input,
Explorer logon, reboots or mixed display adapters.

First publication from the extracted source folder (requires git and authenticated
GitHub CLI for AdamAbuToamy):

    powershell.exe -NoProfile -ExecutionPolicy Bypass -File Release\Publish-GitHub.ps1

This explicitly creates/uses AdamAbuToamy/DeskPages as a public repository, performs
source checks, commits/pushes without force, enables private vulnerability reporting
and dispatches the Windows build. It does not publish a failed build. Watch Actions;
the prerelease includes installer, source, notes and SHA256SUMS.txt. Existing tags
are never overwritten. Repository rules/permissions can block publication and
must be resolved through GitHub; do not bypass them.
