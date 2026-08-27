using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Stagecoach.Core.Interfaces;
using Stagecoach.Core.Models;

namespace Stagecoach.Infrastructure.Azure;

public class KeyVaultCredentialResolver : ICredentialResolver
{
    public async Task<CredentialResolution> ResolveCredentialAsync(StagecoachMachine target, string vaultName = "kv-hcs-vault-01", CancellationToken cancellationToken = default)
    {
        // 1. Explicit tag check
        if (target.Tags.TryGetValue("stagecoach-secret", out var secretId) && !string.IsNullOrWhiteSpace(secretId))
        {
            var val = await GetSecretByIdAsync(secretId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(val))
            {
                var user = target.Tags.TryGetValue("stagecoach-user", out var u) ? u : ".\\Administrator";
                return new CredentialResolution { Source = "KeyVaultTag", Username = user, Password = val };
            }
        }

        // 2. Entra LAPS check
        if (target.Tags.TryGetValue("deviceId", out var deviceId) && !string.IsNullOrWhiteSpace(deviceId))
        {
            var laps = await ResolveLapsAsync(deviceId, cancellationToken);
            if (laps != null) return laps;
        }

        // 3. Domain Key Vault secret
        if (target.DomainType == DomainType.ActiveDirectory && !string.IsNullOrWhiteSpace(target.DomainName))
        {
            var domainSecret = $"domain-{target.DomainName.Replace('.', '-')}-admin";
            var val = await GetSecretByNameAsync(vaultName, domainSecret, cancellationToken);
            if (!string.IsNullOrWhiteSpace(val))
            {
                return new CredentialResolution
                {
                    Source = "DomainKeyVault",
                    Username = $"{target.DomainName}\\Administrator",
                    Password = val
                };
            }
        }

        // 4. Per-VM Key Vault convention
        var vmSecret = $"vm-{target.Name.ToLowerInvariant()}-localadmin";
        var vmVal = await GetSecretByNameAsync(vaultName, vmSecret, cancellationToken);
        if (!string.IsNullOrWhiteSpace(vmVal))
        {
            return new CredentialResolution
            {
                Source = "KeyVaultConvention",
                Username = ".\\Administrator",
                Password = vmVal
            };
        }

        return new CredentialResolution { Source = "None" };
    }

    public async Task<bool> SaveWorkgroupSecretAsync(string machineName, string password, string vaultName = "kv-hcs-vault-01", CancellationToken cancellationToken = default)
    {
        var secretName = $"vm-{machineName.ToLowerInvariant()}-localadmin";
        var psi = new ProcessStartInfo
        {
            FileName = "az",
            Arguments = $"keyvault secret set --vault-name {vaultName} --name \"{secretName}\" --value \"{password}\" -o none",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return false;
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode == 0;
    }

    private static async Task<string?> GetSecretByIdAsync(string secretId, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "az",
            Arguments = $"keyvault secret show --id \"{secretId}\" --query value -o tsv",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return null;
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode == 0 ? output.Trim() : null;
    }

    private static async Task<string?> GetSecretByNameAsync(string vaultName, string secretName, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "az",
            Arguments = $"keyvault secret show --vault-name {vaultName} --name \"{secretName}\" --query value -o tsv",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return null;
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode == 0 ? output.Trim() : null;
    }

    private static async Task<CredentialResolution?> ResolveLapsAsync(string deviceId, CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "az",
                Arguments = "account get-access-token --resource-type ms-graph --query accessToken -o tsv",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return null;
            var token = (await proc.StandardOutput.ReadToEndAsync(cancellationToken)).Trim();
            await proc.WaitForExitAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(token)) return null;

            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var res = await http.GetAsync($"https://graph.microsoft.com/v1.0/directory/deviceLocalCredentials/{deviceId}?$select=credentials", cancellationToken);
            if (!res.IsSuccessStatusCode) return null;

            var json = await res.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("credentials", out var creds) && creds.GetArrayLength() > 0)
            {
                var first = creds.EnumerateArray().First();
                var user = first.GetProperty("accountName").GetString() ?? ".\\Administrator";
                var b64 = first.GetProperty("passwordBase64").GetString();
                if (!string.IsNullOrWhiteSpace(b64))
                {
                    var pass = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                    return new CredentialResolution { Source = "EntraLAPS", Username = user, Password = pass };
                }
            }
        }
        catch { }
        return null;
    }
}
