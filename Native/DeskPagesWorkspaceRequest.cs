// Named delegate keeps array-valued generic arguments out of PowerShell 5.1 syntax.
// This is the production callback contract shared by Agent and the Windows UI.
public delegate string[] DeskPagesWorkspaceRequest(string command, string[] arguments);
