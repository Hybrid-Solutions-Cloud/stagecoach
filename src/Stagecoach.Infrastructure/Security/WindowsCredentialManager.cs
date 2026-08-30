using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Stagecoach.Core;

namespace Stagecoach.Infrastructure.Security;

public sealed class WindowsCredentialManager : IConnectionCredentialStore
{
    private const int CredTypeGeneric = 1;
    private const int CredTypeDomainPassword = 2;
    private const int CredPersistSession = 1;
    private const int CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public string GetTargetName(Guid profileId) => $"Stagecoach/ConnectionIdentity/{profileId:D}";

    public Task SaveAsync(Guid profileId, string username, string password, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Write(GetTargetName(profileId), username, password, CredTypeGeneric, CredPersistLocalMachine);
        return Task.CompletedTask;
    }

    public Task<(string Username, string Password)?> ReadAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Read(GetTargetName(profileId), CredTypeGeneric));
    }

    public Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Delete(GetTargetName(profileId), CredTypeGeneric, ignoreMissing: true);
        return Task.CompletedTask;
    }

    public TemporaryCredentialLease StageRemoteDesktop(string endpoint, string username, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        var target = $"TERMSRV/{endpoint}";
        Write(target, username, password, CredTypeDomainPassword, CredPersistSession);
        return new TemporaryCredentialLease(target, () => Delete(target, CredTypeDomainPassword, ignoreMissing: true));
    }

    private static void Write(string target, string username, string password, int type, int persistence)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows Credential Manager is required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(password);
        var passwordBytes = Encoding.Unicode.GetBytes(password);
        if (passwordBytes.Length > 2560) throw new ArgumentOutOfRangeException(nameof(password), "Credential exceeds the Windows Credential Manager limit.");
        var blob = Marshal.AllocCoTaskMem(passwordBytes.Length);
        try
        {
            Marshal.Copy(passwordBytes, 0, blob, passwordBytes.Length);
            var credential = new NativeCredential
            {
                Type = type,
                TargetName = target,
                CredentialBlobSize = passwordBytes.Length,
                CredentialBlob = blob,
                Persist = persistence,
                UserName = username,
            };
            if (!CredWriteW(ref credential, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            if (passwordBytes.Length > 0) Array.Clear(passwordBytes);
            if (passwordBytes.Length > 0)
                Marshal.Copy(new byte[passwordBytes.Length], 0, blob, passwordBytes.Length);
            Marshal.FreeCoTaskMem(blob);
        }
    }

    private static (string Username, string Password)? Read(string target, int type)
    {
        if (!CredReadW(target, type, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound) return null;
            throw new Win32Exception(error);
        }
        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            var username = credential.UserName ?? string.Empty;
            var password = credential.CredentialBlobSize == 0
                ? string.Empty
                : Marshal.PtrToStringUni(credential.CredentialBlob, credential.CredentialBlobSize / 2) ?? string.Empty;
            return (username, password);
        }
        finally
        {
            CredFree(pointer);
        }
    }

    private static void Delete(string target, int type, bool ignoreMissing)
    {
        if (CredDeleteW(target, type, 0)) return;
        var error = Marshal.GetLastWin32Error();
        if (ignoreMissing && error == ErrorNotFound) return;
        throw new Win32Exception(error);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public int Flags;
        public int Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public nint CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public nint Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWriteW(ref NativeCredential userCredential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredReadW(string target, int type, int reservedFlag, out nint credentialPtr);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDeleteW(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(nint buffer);
}

public sealed class TemporaryCredentialLease(string target, Action dispose) : IDisposable
{
    private Action? _dispose = dispose;
    public string Target { get; } = target;
    public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
}
