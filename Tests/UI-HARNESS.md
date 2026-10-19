# Isolated simple UI harness

Compile SimpleUiHarness.cs with these production Native sources:
DeskPagesWorkspace.cs, DeskPagesSimpleSettings.cs, DeskPagesSimplePreferences.cs,
DeskPagesTheme.cs, DeskPagesWorkspaceControls.cs, DeskPagesWorkspaceRequest.cs,
DeskPagesPageNames.cs. Reference System.Windows.Forms.dll and System.Drawing.dll.

Never include the fixture Host/StartupGate stubs in the production runtime.
The Windows Test-WorkspaceWindows.ps1 compiles and runs the isolated fixture.

It checks compact page opening, a separate settings form, four basic toggles,
no writes on load, a single debounced toggle save, advanced options initially
collapsed, native message-loop responsiveness and no calls to removed tools.
Run in a desktop session; an optional filename prefix enables screenshots.
Mono/Xvfb results exercise that implementation, not Windows DWM or Explorer.
Actual Windows installation/DPI/touchpad/reboot/OLE acceptance remains separate.
