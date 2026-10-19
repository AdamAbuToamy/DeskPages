using System;
// PowerShell converts $null to string.Empty when binding to a string parameter.
// Empty/invalid input disables this optional feature instead of crashing the core.
public static class DeskPagesDragStartupPolicy {
    public static string NormalizeToken(string token){
        Guid value;
        return !string.IsNullOrWhiteSpace(token) && Guid.TryParseExact(token,"N",out value)?value.ToString("N"):null;
    }
}
