using System.Diagnostics;
using Stagecoach.Core.Interfaces;
using Stagecoach.Core.Models;

namespace Stagecoach.Infrastructure.Orchestration;

public class ProcessOrchestrator : IProcessOrchestrator
{
    private readonly Dictionary<string, StagecoachSession> _sessions = new();

    public Task<StagecoachSession> ConnectAsync(StagecoachMachine machine, string username, string? password = null, CancellationToken cancellationToken = default)
    {
        var session = new StagecoachSession
        {
            TargetId = machine.Id,
            TargetName = machine.Name
        };

        if (machine.Kind == TargetKind.ArcServer)
        {
            session.Method = "ArcSshRelay";
            var user = !string.IsNullOrWhiteSpace(username) ? username : (machine.DomainType == DomainType.ActiveDirectory ? $"{machine.DomainName}\\Administrator" : ".\\Administrator");
            var args = $"ssh arc --resource-group \"{machine.ResourceGroup}\" --name \"{machine.Name}\" --local-user \"{user}\" --rdp";

            var psi = new ProcessStartInfo
            {
                FileName = "az",
                Arguments = args,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            };

            var proc = Process.Start(psi);
            if (proc != null)
            {
                session.HelperProcessId = proc.Id;
                session.State = SessionState.Active;
            }
        }
        else if (machine.Kind == TargetKind.AzureVM)
        {
            if (!string.IsNullOrWhiteSpace(machine.BastionHostId))
            {
                session.Method = "BastionNative";
                var parts = machine.BastionHostId.Split('/');
                var bastionName = parts[^1];
                var bastionRg = parts[4];
                var args = $"network bastion rdp --name \"{bastionName}\" --resource-group \"{bastionRg}\" --target-resource-id \"{machine.Id}\"";

                var psi = new ProcessStartInfo
                {
                    FileName = "az",
                    Arguments = args,
                    UseShellExecute = true
                };

                var proc = Process.Start(psi);
                if (proc != null)
                {
                    session.HelperProcessId = proc.Id;
                    session.State = SessionState.Active;
                }
            }
            else
            {
                session.Method = "DirectMstsc";
                var ip = !string.IsNullOrWhiteSpace(machine.PublicIpAddress) ? machine.PublicIpAddress : machine.PrivateIpAddress;
                if (string.IsNullOrWhiteSpace(ip)) ip = machine.Name;

                var proc = Process.Start("mstsc.exe", $"/v:{ip}");
                if (proc != null)
                {
                    session.ClientProcessId = proc.Id;
                    session.State = SessionState.Active;
                }
            }
        }

        _sessions[session.SessionId] = session;
        return Task.FromResult(session);
    }

    public Task<IReadOnlyList<StagecoachSession>> GetActiveSessionsAsync()
    {
        return Task.FromResult<IReadOnlyList<StagecoachSession>>(_sessions.Values.ToList());
    }

    public Task DisconnectSessionAsync(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            if (session.HelperProcessId > 0)
            {
                try { Process.GetProcessById(session.HelperProcessId).Kill(); } catch { }
            }
            if (session.ClientProcessId > 0)
            {
                try { Process.GetProcessById(session.ClientProcessId).Kill(); } catch { }
            }
            session.State = SessionState.Disconnected;
        }
        return Task.CompletedTask;
    }
}
