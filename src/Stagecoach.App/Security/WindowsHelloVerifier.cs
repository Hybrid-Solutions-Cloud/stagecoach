using Windows.Security.Credentials.UI;

namespace Stagecoach.App.Security;

public enum UserVerificationResult
{
    Verified,
    Canceled,
    Failed,
    NotConfigured,
    DisabledByPolicy,
    RemoteSessionUnavailable,
    Unavailable,
}

/// <summary>
/// Windows Hello, or the Windows account password when Hello is not enrolled — both are handled by
/// <see cref="UserConsentVerifier"/>, which is why this is the right gate for a Windows-account
/// owner. Follows the same shape as Vault Prospector's verification service, including telling a
/// remote session apart from a machine with no biometric hardware: over RDP the prompt cannot be
/// shown, and the operator needs to be told that rather than "unavailable".
/// </summary>
public sealed class WindowsHelloVerifier(Func<nint> windowHandleProvider)
{
    // SM_REMOTESESSION. Windows Hello cannot prompt over a remote session, and that has to be told
    // apart from a machine that simply has no biometric hardware.
    private const int SM_REMOTESESSION = 0x1000;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    public static bool IsRemoteSession =>
        OperatingSystem.IsWindows() && GetSystemMetrics(SM_REMOTESESSION) != 0;

    public async Task<UserVerificationResult> VerifyAsync(string reason, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) return UserVerificationResult.Unavailable;
        cancellationToken.ThrowIfCancellationRequested();

        UserConsentVerifierAvailability availability;
        try
        {
            availability = await UserConsentVerifier.CheckAvailabilityAsync();
        }
        catch (Exception)
        {
            return UserVerificationResult.Unavailable;
        }

        var unavailable = availability switch
        {
            UserConsentVerifierAvailability.Available => (UserVerificationResult?)null,
            UserConsentVerifierAvailability.NotConfiguredForUser => UserVerificationResult.NotConfigured,
            UserConsentVerifierAvailability.DisabledByPolicy => UserVerificationResult.DisabledByPolicy,
            UserConsentVerifierAvailability.DeviceNotPresent when IsRemoteSession =>
                UserVerificationResult.RemoteSessionUnavailable,
            _ => UserVerificationResult.Unavailable,
        };
        if (unavailable is not null) return unavailable.Value;

        var handle = windowHandleProvider();
        if (handle == 0) return UserVerificationResult.Unavailable;

        try
        {
            var result = await UserConsentVerifierInterop.RequestVerificationForWindowAsync(handle, reason);
            cancellationToken.ThrowIfCancellationRequested();
            return result switch
            {
                UserConsentVerificationResult.Verified => UserVerificationResult.Verified,
                UserConsentVerificationResult.Canceled => UserVerificationResult.Canceled,
                UserConsentVerificationResult.NotConfiguredForUser => UserVerificationResult.NotConfigured,
                UserConsentVerificationResult.DisabledByPolicy => UserVerificationResult.DisabledByPolicy,
                UserConsentVerificationResult.DeviceNotPresent when IsRemoteSession =>
                    UserVerificationResult.RemoteSessionUnavailable,
                UserConsentVerificationResult.DeviceNotPresent or UserConsentVerificationResult.DeviceBusy =>
                    UserVerificationResult.Unavailable,
                _ => UserVerificationResult.Failed,
            };
        }
        catch (Exception)
        {
            return UserVerificationResult.Unavailable;
        }
    }

    public static string Describe(UserVerificationResult result) => result switch
    {
        UserVerificationResult.Verified => "Verified.",
        UserVerificationResult.Canceled => "Sign-in was cancelled.",
        UserVerificationResult.NotConfigured =>
            "Windows Hello is not set up for this account. Use the passphrase instead, or enrol Hello in Windows Settings.",
        UserVerificationResult.DisabledByPolicy =>
            "Windows Hello is disabled by policy on this machine. Use the passphrase instead.",
        UserVerificationResult.RemoteSessionUnavailable =>
            "Windows Hello cannot prompt inside a remote session. Use the passphrase instead.",
        UserVerificationResult.Unavailable =>
            "Windows Hello is unavailable on this machine. Use the passphrase instead.",
        _ => "Windows could not verify you.",
    };
}
