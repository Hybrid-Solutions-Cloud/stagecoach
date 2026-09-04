using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Stagecoach.App.Security;

/// <summary>
/// Asks Windows itself to verify the operator with their own Windows credentials, and confirms the
/// credentials that were entered belong to the account already signed in.
/// <para>
/// This exists because <b>Windows Hello cannot prompt inside a remote session</b>, and Stagecoach is
/// used over RDP. Vault Prospector solves it the same way — a credential prompt, never an
/// application-specific secret. Nothing typed here is stored: the password buffer is zeroed and
/// freed, and the only thing kept is the yes-or-no answer.
/// </para>
/// </summary>
public sealed class WindowsCredentialVerifier(Func<nint> windowHandleProvider)
{
    private const int ErrorCancelled = 1223;
    private const int Logon32LogonNetwork = 3;
    private const int Logon32ProviderDefault = 0;
    private const uint CredUiWinGeneric = 0x1;
    private const int MaximumUserNameCharacters = 513;
    private const int MaximumDomainCharacters = 256;
    private const int MaximumPasswordCharacters = 256;

    public static bool IsAvailable => OperatingSystem.IsWindows();

    public Task<UserVerificationResult> VerifyAsync(string reason)
    {
        if (!OperatingSystem.IsWindows()) return Task.FromResult(UserVerificationResult.Unavailable);
        var handle = windowHandleProvider();
        return handle == 0
            ? Task.FromResult(UserVerificationResult.Unavailable)
            : Task.Run(() => Verify(handle, reason));
    }

    private static UserVerificationResult Verify(nint windowHandle, string reason)
    {
        nint authenticationBuffer = 0;
        nint userNameBuffer = 0;
        nint domainBuffer = 0;
        nint passwordBuffer = 0;
        uint authenticationBufferSize = 0;
        try
        {
            var info = new CredUiInfo
            {
                Size = Marshal.SizeOf<CredUiInfo>(),
                Parent = windowHandle,
                CaptionText = "Unlock Stagecoach",
                MessageText = $"{reason}. Enter the credentials for your current Windows account.",
            };
            uint authenticationPackage = 0;
            var save = false;
            var promptResult = CredUIPromptForWindowsCredentials(
                ref info, 0, ref authenticationPackage, 0, 0,
                out authenticationBuffer, out authenticationBufferSize, ref save, CredUiWinGeneric);
            if (promptResult == ErrorCancelled) return UserVerificationResult.Canceled;
            if (promptResult != 0) return UserVerificationResult.CredentialPromptUnavailable;

            userNameBuffer = AllocateCharacters(MaximumUserNameCharacters);
            domainBuffer = AllocateCharacters(MaximumDomainCharacters);
            passwordBuffer = AllocateCharacters(MaximumPasswordCharacters);
            uint userNameLength = MaximumUserNameCharacters;
            uint domainLength = MaximumDomainCharacters;
            uint passwordLength = MaximumPasswordCharacters;
            var unpacked = CredUnPackAuthenticationBuffer(
                0, authenticationBuffer, authenticationBufferSize,
                userNameBuffer, ref userNameLength,
                domainBuffer, ref domainLength,
                passwordBuffer, ref passwordLength);
            if (!unpacked) return UserVerificationResult.CredentialCheckFailed;

            var userName = Marshal.PtrToStringUni(userNameBuffer) ?? string.Empty;
            var domain = Marshal.PtrToStringUni(domainBuffer);
            if (string.IsNullOrWhiteSpace(userName)) return UserVerificationResult.CredentialCheckFailed;

            var logon = NormalizeLogonName(userName, domain);
            var loggedOn = LogonUser(
                logon.UserName, logon.Domain, passwordBuffer,
                Logon32LogonNetwork, Logon32ProviderDefault, out var token);

            using (token)
            {
                if (!loggedOn) return UserVerificationResult.CredentialCheckFailed;

                // The credentials must be the account already signed in — otherwise anyone with any
                // valid account on the machine could open somebody else's estate.
                using var verifiedIdentity = new WindowsIdentity(token.DangerousGetHandle());
                using var currentIdentity = WindowsIdentity.GetCurrent();
                var verifiedSid = verifiedIdentity.User;
                var currentSid = currentIdentity.User;
                return verifiedSid is not null && currentSid is not null && verifiedSid.Equals(currentSid)
                    ? UserVerificationResult.Verified
                    : UserVerificationResult.CredentialCheckFailed;
            }
        }
        catch (Exception exception) when (
            exception is Win32Exception or ExternalException or InvalidOperationException or UnauthorizedAccessException)
        {
            return UserVerificationResult.CredentialCheckFailed;
        }
        finally
        {
            ZeroAndFree(passwordBuffer, MaximumPasswordCharacters * sizeof(char));
            ZeroAndFree(domainBuffer, MaximumDomainCharacters * sizeof(char));
            ZeroAndFree(userNameBuffer, MaximumUserNameCharacters * sizeof(char));
            ZeroAndFreeCoTaskMem(
                authenticationBuffer,
                authenticationBuffer == 0 ? 0 : checked((int)authenticationBufferSize));
        }
    }

    internal static (string UserName, string? Domain) NormalizeLogonName(string userName, string? domain)
    {
        var normalizedDomain = string.IsNullOrWhiteSpace(domain) ? null : domain;
        if (normalizedDomain is not null) return (userName, normalizedDomain);

        var separatorIndex = userName.IndexOf('\\');
        if (separatorIndex <= 0 || separatorIndex == userName.Length - 1) return (userName, null);
        return (userName[(separatorIndex + 1)..], userName[..separatorIndex]);
    }

    private static nint AllocateCharacters(int characterCount) =>
        Marshal.AllocHGlobal(characterCount * sizeof(char));

    private static void ZeroAndFree(nint buffer, int byteCount)
    {
        if (buffer == 0) return;
        Zero(buffer, byteCount);
        Marshal.FreeHGlobal(buffer);
    }

    private static void ZeroAndFreeCoTaskMem(nint buffer, int byteCount)
    {
        if (buffer == 0) return;
        Zero(buffer, byteCount);
        Marshal.FreeCoTaskMem(buffer);
    }

    private static void Zero(nint buffer, int byteCount)
    {
        for (var offset = 0; offset < byteCount; offset++) Marshal.WriteByte(buffer, offset, 0);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CredUiInfo
    {
        public int Size;
        public nint Parent;
        [MarshalAs(UnmanagedType.LPWStr)] public string MessageText;
        [MarshalAs(UnmanagedType.LPWStr)] public string CaptionText;
        public nint Banner;
    }

    [DllImport("credui.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int CredUIPromptForWindowsCredentials(
        ref CredUiInfo uiInfo,
        int authenticationError,
        ref uint authenticationPackage,
        nint inAuthenticationBuffer,
        uint inAuthenticationBufferSize,
        out nint outAuthenticationBuffer,
        out uint outAuthenticationBufferSize,
        ref bool save,
        uint flags);

    [DllImport("credui.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredUnPackAuthenticationBuffer(
        uint flags,
        nint authenticationBuffer,
        uint authenticationBufferSize,
        nint userName,
        ref uint maximumUserNameLength,
        nint domainName,
        ref uint maximumDomainNameLength,
        nint password,
        ref uint maximumPasswordLength);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LogonUser(
        string userName,
        string? domain,
        nint password,
        int logonType,
        int logonProvider,
        out SafeAccessTokenHandle token);
}
