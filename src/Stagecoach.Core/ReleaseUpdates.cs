using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Stagecoach.Core;

public enum ReleaseUpdateAvailability
{
    Unknown,
    Current,
    Available,
    DevelopmentBuild,
}

public sealed record ReleaseUpdateInfo(
    string CurrentVersion,
    string LatestVersion,
    ReleaseUpdateAvailability Availability,
    string ReleaseName,
    string ReleaseNotes,
    Uri ReleasePageUri,
    Uri PackageUri,
    Uri ChecksumUri,
    string PackageName,
    long PackageSize,
    string ExpectedSha256,
    DateTimeOffset PublishedAt);

public sealed record VerifiedReleaseUpdate(
    ReleaseUpdateInfo Release,
    string InstallerPath,
    string Sha256);

public interface IUpdateInstallerLauncher
{
    void Launch(string installerPath);
}

public interface IReleaseUpdateService
{
    Task<ReleaseUpdateInfo> CheckAsync(CancellationToken cancellationToken = default);

    Task<VerifiedReleaseUpdate> DownloadAndVerifyAsync(
        ReleaseUpdateInfo release,
        CancellationToken cancellationToken = default);

    Task LaunchAsync(VerifiedReleaseUpdate update, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads signed Stagecoach releases from the trusted release repository, verifies the published
/// installer against its authenticated digest, and hands it to Windows Installer. Nothing is
/// executed until the bytes on disk hash to the value GitHub authenticated.
/// </summary>
public sealed class GitHubReleaseUpdateService(
    HttpClient httpClient,
    string updateDirectory,
    string currentVersion,
    IUpdateInstallerLauncher installerLauncher) : IReleaseUpdateService
{
    private const string ExpectedPublisher = "hcs-platform-app[bot]";
    private const string ReleaseRepository = "Hybrid-Solutions-Cloud/stagecoach-releases";
    private const string UserAgent = "Stagecoach-UpdateClient/1.0";

    private static readonly Uri ReleasesApiUri =
        new($"https://api.github.com/repos/{ReleaseRepository}/releases?per_page=20");

    private static readonly string ReleaseDownloadPrefix =
        $"https://github.com/{ReleaseRepository}/releases/download/";

    private static readonly string ReleasePagePrefix =
        $"https://github.com/{ReleaseRepository}/releases/tag/";

    private readonly string _updateDirectory = Path.GetFullPath(updateDirectory);

    public async Task<ReleaseUpdateInfo> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesApiUri);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.UserAgent.ParseAdd(UserAgent);
        using var response = await httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 2_097_152)
            throw new InvalidDataException("Release metadata exceeded the supported size.");

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(
            content,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32,
            },
            cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Release metadata did not contain a release list.");

        ReleaseCandidate? selected = null;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            var candidate = TryReadCandidate(release);
            if (candidate is null ||
                (selected is not null && candidate.Version.CompareTo(selected.Version) <= 0))
                continue;
            selected = candidate;
        }

        if (selected is null)
            throw new InvalidDataException("No trusted supported Stagecoach release was found.");

        var parsedCurrent = ProductVersion.TryParse(NormalizeVersion(currentVersion));
        var availability = parsedCurrent is null
            ? ReleaseUpdateAvailability.DevelopmentBuild
            : selected.Version.CompareTo(parsedCurrent.Value) > 0
                ? ReleaseUpdateAvailability.Available
                : ReleaseUpdateAvailability.Current;

        return new ReleaseUpdateInfo(
            currentVersion,
            selected.Version.ToString(),
            availability,
            selected.ReleaseName,
            selected.ReleaseNotes,
            selected.ReleasePageUri,
            selected.PackageUri,
            selected.ChecksumUri,
            selected.PackageName,
            selected.PackageSize,
            selected.ExpectedSha256,
            selected.PublishedAt);
    }

    public async Task<VerifiedReleaseUpdate> DownloadAndVerifyAsync(
        ReleaseUpdateInfo release,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (release.Availability != ReleaseUpdateAvailability.Available)
            throw new InvalidOperationException("Only a newer trusted release can be downloaded.");

        ValidateReleaseInfo(release);
        var checksumText = await ReadSmallTextAsync(release.ChecksumUri, cancellationToken);
        var checksum = ParseChecksum(checksumText, release.PackageName);
        if (!string.Equals(checksum, release.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "The release checksum did not match the authenticated GitHub asset digest.");

        var versionDirectory = Path.GetFullPath(Path.Combine(_updateDirectory, release.LatestVersion));
        EnsureContainedPath(_updateDirectory, versionDirectory);
        Directory.CreateDirectory(_updateDirectory);
        RejectReparsePoint(_updateDirectory);
        Directory.CreateDirectory(versionDirectory);
        RejectReparsePoint(versionDirectory);
        var installerPath = Path.GetFullPath(Path.Combine(versionDirectory, release.PackageName));
        EnsureContainedPath(versionDirectory, installerPath);

        if (File.Exists(installerPath))
        {
            var existingHash = await HashFileAsync(installerPath, cancellationToken);
            if (string.Equals(existingHash, checksum, StringComparison.OrdinalIgnoreCase))
                return new VerifiedReleaseUpdate(release, installerPath, existingHash);
        }

        var partialPath = Path.Combine(versionDirectory, $".{Guid.NewGuid():N}.partial");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, release.PackageUri);
            request.Headers.Accept.ParseAdd("application/octet-stream");
            request.Headers.UserAgent.ParseAdd(UserAgent);
            using var response = await httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } contentLength &&
                contentLength != release.PackageSize)
                throw new InvalidDataException(
                    "The downloaded installer size did not match authenticated release metadata.");

            string actualHash;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = new FileStream(
                             partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             131_072,
                             FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[131_072];
                long totalBytes = 0;
                while (true)
                {
                    var bytesRead = await source.ReadAsync(buffer, cancellationToken);
                    if (bytesRead == 0) break;
                    totalBytes += bytesRead;
                    if (totalBytes > release.PackageSize)
                        throw new InvalidDataException(
                            "The downloaded installer exceeded its authenticated size.");
                    hash.AppendData(buffer, 0, bytesRead);
                    await target.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                }

                await target.FlushAsync(cancellationToken);
                if (totalBytes != release.PackageSize)
                    throw new InvalidDataException("The downloaded installer was incomplete.");

                actualHash = Convert.ToHexString(hash.GetHashAndReset());
                if (!CryptographicOperations.FixedTimeEquals(
                        Convert.FromHexString(actualHash), Convert.FromHexString(checksum)))
                    throw new InvalidDataException(
                        "The downloaded installer failed SHA-256 verification.");
            }

            File.Move(partialPath, installerPath, true);
            return new VerifiedReleaseUpdate(release, installerPath, actualHash);
        }
        finally
        {
            if (File.Exists(partialPath)) File.Delete(partialPath);
        }
    }

    public async Task LaunchAsync(VerifiedReleaseUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ValidateReleaseInfo(update.Release);
        var installerPath = Path.GetFullPath(update.InstallerPath);
        EnsureContainedPath(_updateDirectory, installerPath);
        if (!string.Equals(Path.GetFileName(installerPath), update.Release.PackageName, StringComparison.Ordinal) ||
            !File.Exists(installerPath))
            throw new InvalidDataException("The verified installer is no longer available.");

        var actualHash = await HashFileAsync(installerPath, cancellationToken);
        if (!string.Equals(actualHash, update.Sha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(actualHash, update.Release.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The installer changed after verification.");

        installerLauncher.Launch(installerPath);
    }

    internal static string BuildPackageName(string version) => $"Stagecoach-{version}-win-x64.msi";

    private static ReleaseCandidate? TryReadCandidate(JsonElement release)
    {
        try
        {
            if (release.GetProperty("draft").GetBoolean()) return null;
            var publisher = release.GetProperty("author").GetProperty("login").GetString();
            if (!string.Equals(publisher, ExpectedPublisher, StringComparison.Ordinal)) return null;

            var tag = release.GetProperty("tag_name").GetString();
            var normalizedVersion = NormalizeVersion(tag);
            var version = ProductVersion.TryParse(normalizedVersion);
            if (version is null) return null;

            var releaseName = release.GetProperty("name").GetString() ?? $"Stagecoach {normalizedVersion}";
            var releaseNotes = release.GetProperty("body").GetString() ?? "No release notes were provided.";
            if (releaseName.Contains("withdrawn", StringComparison.OrdinalIgnoreCase) ||
                releaseNotes.Contains("withdrawn", StringComparison.OrdinalIgnoreCase))
                return null;

            var packageName = BuildPackageName(normalizedVersion);
            var checksumName = $"{packageName}.sha256";
            var sigstoreName = $"{packageName}.sigstore.json";
            JsonElement? package = null;
            JsonElement? checksum = null;
            var hasSigstoreBundle = false;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                if (string.Equals(name, packageName, StringComparison.Ordinal)) package = asset;
                else if (string.Equals(name, checksumName, StringComparison.Ordinal)) checksum = asset;
                else if (string.Equals(name, sigstoreName, StringComparison.Ordinal)) hasSigstoreBundle = true;
            }

            if (package is null || checksum is null || !hasSigstoreBundle) return null;

            var packageUri = ReadTrustedDownloadUri(package.Value);
            var checksumUri = ReadTrustedDownloadUri(checksum.Value);
            var releasePageUri = new Uri(
                release.GetProperty("html_url").GetString() ?? string.Empty, UriKind.Absolute);
            if (!releasePageUri.AbsoluteUri.StartsWith(ReleasePagePrefix, StringComparison.Ordinal))
                return null;

            var digest = package.Value.GetProperty("digest").GetString();
            if (digest is null ||
                !digest.StartsWith("sha256:", StringComparison.Ordinal) ||
                !IsSha256(digest[7..]))
                return null;

            var packageSize = package.Value.GetProperty("size").GetInt64();
            if (packageSize is < 1 or > 536_870_912) return null;
            var checksumSize = checksum.Value.GetProperty("size").GetInt64();
            if (checksumSize is < 1 or > 4096) return null;

            return new ReleaseCandidate(
                version.Value,
                releaseName,
                releaseNotes.Length <= 8_000 ? releaseNotes : releaseNotes[..8_000],
                releasePageUri,
                packageUri,
                checksumUri,
                packageName,
                packageSize,
                digest[7..].ToUpperInvariant(),
                release.GetProperty("published_at").GetDateTimeOffset());
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or FormatException or KeyNotFoundException or UriFormatException)
        {
            return null;
        }
    }

    private async Task<string> ReadSmallTextAsync(Uri uri, CancellationToken cancellationToken)
    {
        ValidateDownloadUri(uri);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd("application/octet-stream");
        request.Headers.UserAgent.ParseAdd(UserAgent);
        using var response = await httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 4096)
            throw new InvalidDataException("The release checksum exceeded the supported size.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: false, leaveOpen: false);
        var text = await reader.ReadToEndAsync(cancellationToken);
        if (text.Length > 4096)
            throw new InvalidDataException("The release checksum exceeded the supported size.");
        return text;
    }

    private static Uri ReadTrustedDownloadUri(JsonElement asset)
    {
        var uri = new Uri(
            asset.GetProperty("browser_download_url").GetString() ?? string.Empty, UriKind.Absolute);
        ValidateDownloadUri(uri);
        return uri;
    }

    private static void ValidateReleaseInfo(ReleaseUpdateInfo release)
    {
        var version = ProductVersion.TryParse(release.LatestVersion);
        if (version is null ||
            !string.Equals(release.PackageName, BuildPackageName(release.LatestVersion), StringComparison.Ordinal) ||
            release.PackageSize is < 1 or > 536_870_912 ||
            !IsSha256(release.ExpectedSha256))
            throw new InvalidDataException("Release metadata failed validation.");

        ValidateDownloadUri(release.PackageUri);
        ValidateDownloadUri(release.ChecksumUri);
        if (!release.ReleasePageUri.AbsoluteUri.StartsWith(ReleasePagePrefix, StringComparison.Ordinal))
            throw new InvalidDataException("The release page is outside the trusted repository.");
    }

    private static void ValidateDownloadUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.AbsoluteUri.StartsWith(ReleaseDownloadPrefix, StringComparison.Ordinal))
            throw new InvalidDataException("A release asset URI was outside the trusted repository.");
    }

    private static string ParseChecksum(string text, string packageName)
    {
        var parts = text.Trim().Split(
            [' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 ||
            !IsSha256(parts[0]) ||
            !string.Equals(parts[1].TrimStart('*'), packageName, StringComparison.Ordinal))
            throw new InvalidDataException("The release checksum file was malformed.");
        return parts[0].ToUpperInvariant();
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 131_072,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static string NormalizeVersion(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.StartsWith('v') || normalized.StartsWith('V')) normalized = normalized[1..];
        var buildIndex = normalized.IndexOf('+', StringComparison.Ordinal);
        return buildIndex >= 0 ? normalized[..buildIndex] : normalized;
    }

    private static void EnsureContainedPath(string root, string candidate)
    {
        var normalizedRoot =
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        var normalizedCandidate = Path.GetFullPath(candidate);
        if (!normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The update path escaped the controlled update directory.");
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The update directory cannot be a reparse point.");
    }

    private sealed record ReleaseCandidate(
        ProductVersion Version,
        string ReleaseName,
        string ReleaseNotes,
        Uri ReleasePageUri,
        Uri PackageUri,
        Uri ChecksumUri,
        string PackageName,
        long PackageSize,
        string ExpectedSha256,
        DateTimeOffset PublishedAt);

    internal readonly record struct ProductVersion(
        int Major, int Minor, int Patch, string? Prerelease) : IComparable<ProductVersion>
    {
        public static ProductVersion? TryParse(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 64) return null;
            var separator = value.IndexOf('-', StringComparison.Ordinal);
            var numeric = separator >= 0 ? value[..separator] : value;
            var prerelease = separator >= 0 ? value[(separator + 1)..] : null;
            var segments = numeric.Split('.');
            if (segments.Length != 3 ||
                !int.TryParse(segments[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
                !int.TryParse(segments[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
                !int.TryParse(segments[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch) ||
                (prerelease is not null &&
                 (prerelease.Length == 0 ||
                  prerelease.Any(character =>
                      !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-'))))
                return null;
            return new ProductVersion(major, minor, patch, prerelease);
        }

        public int CompareTo(ProductVersion other)
        {
            var numeric = Major.CompareTo(other.Major);
            if (numeric != 0) return numeric;
            numeric = Minor.CompareTo(other.Minor);
            if (numeric != 0) return numeric;
            numeric = Patch.CompareTo(other.Patch);
            if (numeric != 0) return numeric;
            if (Prerelease is null) return other.Prerelease is null ? 0 : 1;
            if (other.Prerelease is null) return -1;

            var left = Prerelease.Split('.');
            var right = other.Prerelease.Split('.');
            for (var index = 0; index < Math.Max(left.Length, right.Length); index++)
            {
                if (index >= left.Length) return -1;
                if (index >= right.Length) return 1;
                var leftNumeric = int.TryParse(
                    left[index], NumberStyles.None, CultureInfo.InvariantCulture, out var leftValue);
                var rightNumeric = int.TryParse(
                    right[index], NumberStyles.None, CultureInfo.InvariantCulture, out var rightValue);
                var comparison = leftNumeric && rightNumeric
                    ? leftValue.CompareTo(rightValue)
                    : leftNumeric ? -1
                    : rightNumeric ? 1
                    : string.Compare(left[index], right[index], StringComparison.Ordinal);
                if (comparison != 0) return comparison;
            }

            return 0;
        }

        public override string ToString() =>
            Prerelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{Prerelease}";
    }
}
