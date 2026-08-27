namespace Stagecoach.Core.Models;

public enum SessionState
{
    Starting,
    Active,
    Disconnected,
    Failed
}

public class StagecoachSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString();
    public string TargetId { get; set; } = string.Empty;
    public string TargetName { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public int LocalPort { get; set; }
    public int HelperProcessId { get; set; }
    public int ClientProcessId { get; set; }
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public SessionState State { get; set; } = SessionState.Starting;
    public string? ErrorMessage { get; set; }
}
