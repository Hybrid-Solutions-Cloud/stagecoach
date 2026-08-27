namespace Stagecoach.Core.Models;

public class CredentialResolution
{
    public string Source { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool IsResolved => !string.IsNullOrWhiteSpace(Password);
}
