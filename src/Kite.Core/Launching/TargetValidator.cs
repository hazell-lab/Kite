using Kite.Core.Storage;

namespace Kite.Core.Launching;

/// <summary>the result of checking a target. error is null when the target is fine.</summary>
public sealed record TargetCheck(bool IsValid, string ResolvedTarget, string? Error)
{
    public static TargetCheck Ok(string target) => new(true, target, null);

    public static TargetCheck Fail(string target, string error) => new(false, target, error);
}

/// <summary>checks that paths and web addresses are safe and exist before opening them.</summary>
public static class TargetValidator
{
    /// <summary>true only for full http or https addresses with a host name.</summary>
    public static bool IsWebAddress(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
               !string.IsNullOrEmpty(uri.Host);
    }

    /// <summary>
    /// turn "example.com" into "https://example.com". returns null when it
    /// still is not a valid web address.
    /// </summary>
    public static string? NormalizeWebAddress(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        if (IsWebAddress(trimmed))
        {
            return trimmed;
        }

        if (!trimmed.Contains("://", StringComparison.Ordinal) && !trimmed.Contains(' '))
        {
            var withScheme = "https://" + trimmed;
            if (IsWebAddress(withScheme) && withScheme.Contains('.'))
            {
                return withScheme;
            }
        }

        return null;
    }

    /// <summary>replace things like %userprofile% and trim quotes.</summary>
    public static string ExpandPath(string path) =>
        Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));

    /// <summary>check an alias target for its kind.</summary>
    public static TargetCheck Check(AliasKind kind, string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return TargetCheck.Fail(string.Empty, "The target is empty.");
        }

        switch (kind)
        {
            case AliasKind.Website:
            {
                var url = NormalizeWebAddress(target);
                return url is null
                    ? TargetCheck.Fail(target, "Enter a full web address that starts with http:// or https://.")
                    : TargetCheck.Ok(url);
            }

            case AliasKind.Folder:
            {
                var path = ExpandPath(target);
                if (!IsFullPath(path))
                {
                    return TargetCheck.Fail(path, "Enter the full folder path, for example C:\\Users\\you\\Projects.");
                }

                return Directory.Exists(path)
                    ? TargetCheck.Ok(path)
                    : TargetCheck.Fail(path, $"The folder was not found: {path}");
            }

            case AliasKind.File:
            case AliasKind.Application:
            {
                var path = ExpandPath(target);
                if (!IsFullPath(path))
                {
                    return TargetCheck.Fail(path, "Enter the full file path, for example C:\\Tools\\app.exe.");
                }

                if (Directory.Exists(path))
                {
                    return TargetCheck.Fail(path, "This is a folder. Choose the folder type instead.");
                }

                return File.Exists(path)
                    ? TargetCheck.Ok(path)
                    : TargetCheck.Fail(path, $"The file was not found: {path}");
            }

            default:
                return TargetCheck.Fail(target, "Unknown target type.");
        }
    }

    private static bool IsFullPath(string path)
    {
        try
        {
            return Path.IsPathFullyQualified(path);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
