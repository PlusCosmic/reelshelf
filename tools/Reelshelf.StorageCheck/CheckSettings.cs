using Microsoft.Extensions.Configuration;
using Reelshelf.Video.R2;

namespace Reelshelf.StorageCheck;

/// <summary>A command line or settings file the check can't run with; the message says what to change.</summary>
public sealed class UsageException(string message) : Exception(message);

/// <summary>Where a setting's value came from, by name only; values are never printed.</summary>
public sealed record SettingSource(string Name, string Source);

/// <summary>
/// What the check runs against: the store as the API would read it, the optional evidence bucket and distribution,
/// and the check's own options. Only the settings the API reads for storage are taken from the environment, so an
/// instance environment that also carries <c>R2__*</c> and every other app secret (Infisical's <c>run</c>) gives the
/// same store the API would get once <c>R2__*</c> is removed, rather than the API's "configured twice" refusal.
/// </summary>
public sealed record CheckSettings(
    R2Options Store,
    string? EvidenceBucket,
    string? DistributionId,
    string SiteOrigin,
    string OtherOrigin,
    string? MediaConnectHost,
    int? MediaConnectPort,
    bool SkipCdn,
    bool PurgeWait,
    TimeSpan PurgeWaitLimit,
    IReadOnlyList<SettingSource> Sources,
    IReadOnlyList<string> Ignored)
{
    public const string DefaultOtherOrigin = "https://storage-check.invalid";
    public static readonly TimeSpan DefaultPurgeWaitLimit = TimeSpan.FromMinutes(5);

    /// <summary>The configuration keys the check reads, as the API spells them.</summary>
    public static readonly IReadOnlyList<string> StoreKeys =
    [
        "Storage:Endpoint", "Storage:Region", "Storage:AccessKeyId", "Storage:SecretAccessKey", "Storage:Bucket",
        "Storage:MediaBaseUrl", "Moderation:EvidenceBucket", "Moderation:CloudFront:DistributionId"
    ];

    /// <summary>Settings that may come from the environment or an env file, never the command line.</summary>
    public static readonly IReadOnlySet<string> SecretKeys =
        new HashSet<string>(["Storage:AccessKeyId", "Storage:SecretAccessKey"], StringComparer.OrdinalIgnoreCase);

    /// <summary>Command-line spellings of the non-secret store settings.</summary>
    private static readonly Dictionary<string, string> StoreOptions = new(StringComparer.Ordinal)
    {
        ["--storage-endpoint"] = "Storage:Endpoint",
        ["--storage-region"] = "Storage:Region",
        ["--storage-bucket"] = "Storage:Bucket",
        ["--storage-media-base-url"] = "Storage:MediaBaseUrl",
        ["--evidence-bucket"] = "Moderation:EvidenceBucket",
        ["--distribution-id"] = "Moderation:CloudFront:DistributionId"
    };

    private static readonly HashSet<string> Flags =
        new(["--skip-cdn", "--no-purge-wait", "--help", "-h"], StringComparer.Ordinal);

    private static readonly HashSet<string> ValueOptions =
        new(["--env-file", "--site-origin", "--other-origin", "--media-connect-host", "--purge-wait-seconds"],
            StringComparer.Ordinal);

    public const string Usage =
        """
        Usage: dotnet run --project tools/Reelshelf.StorageCheck -- --site-origin <https origin> [options]

        Runs the API's and the worker's own storage code against the bucket and CDN the settings name, writing only
        under fresh random keys and deleting all of it afterwards (README.md beside the project).

        Store settings, read as the API reads them, from the environment or --env-file (later wins: the
        environment, then the env file, then the command line):
          Storage__Region, Storage__Bucket, Storage__AccessKeyId, Storage__SecretAccessKey, Storage__MediaBaseUrl,
          Storage__Endpoint (an S3-compatible endpoint instead of AWS), Moderation__EvidenceBucket (optional),
          Moderation__CloudFront__DistributionId (optional)
        Every other variable, R2__* included, is ignored.

        Options:
          --env-file <path>             KEY=VALUE lines (such as aws/storage/secrets.sh writes); no shell evaluation
          --site-origin <origin>        the site whose browsers upload (CORS), e.g. https://reelshelf.app; required
          --other-origin <origin>       an origin CORS must refuse (default https://storage-check.invalid)
          --media-connect-host <host>   fetch Storage:MediaBaseUrl's paths from this host (e.g. dxxxx.cloudfront.net)
                                        while still sending, and validating the certificate for, the media hostname;
                                        host[:port]
          --skip-cdn                    don't fetch anything through the media hostname
          --no-purge-wait               don't wait for the invalidation to take effect
          --purge-wait-seconds <n>      how long to wait for it (default 300)
          --storage-region, --storage-bucket, --storage-media-base-url, --storage-endpoint, --evidence-bucket,
          --distribution-id <value>     the non-secret store settings; the key is never taken from the command line

        Exit codes: 0 every check that ran passed and nothing of the run is left; 1 a check failed; 2 usage;
        3 cleanup could not be verified (the keys left are listed); 130 interrupted (cleanup still ran).
        """;

    /// <summary>
    /// Reads the settings from <paramref name="args"/>, <paramref name="environment"/> and the env file the
    /// arguments name (read with <paramref name="readFile"/>). Null when only help was asked for.
    /// </summary>
    public static CheckSettings? Parse(IReadOnlyList<string> args, IReadOnlyDictionary<string, string> environment,
        Func<string, IReadOnlyList<string>> readFile)
    {
        Dictionary<string, string> options = ParseArguments(args, out HashSet<string> flags,
            out Dictionary<string, string> storeArguments);
        if (flags.Contains("--help") || flags.Contains("-h"))
        {
            return null;
        }

        Dictionary<string, (string Value, string Source)> store = new(StringComparer.OrdinalIgnoreCase);
        List<string> ignored = [];
        foreach ((string name, string value) in environment.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (StoreKey(name) is { } key)
            {
                store[key] = (value, "environment");
            }
            else if (name.StartsWith("R2__", StringComparison.OrdinalIgnoreCase) ||
                     name.StartsWith("R2:", StringComparison.OrdinalIgnoreCase))
            {
                ignored.Add(name);
            }
        }

        if (options.TryGetValue("--env-file", out string? envFile))
        {
            IReadOnlyList<string> lines;
            try
            {
                lines = readFile(envFile);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                throw new UsageException($"--env-file: can't read {envFile} ({error.GetType().Name})");
            }

            foreach ((string name, string value) in ParseEnvFile(lines, envFile))
            {
                if (StoreKey(name) is { } key)
                {
                    store[key] = (value, $"--env-file {Path.GetFileName(envFile)}");
                }
                else if (name.StartsWith("R2__", StringComparison.OrdinalIgnoreCase))
                {
                    ignored.Add($"{name} (env file)");
                }
            }
        }

        foreach ((string key, string value) in storeArguments)
        {
            store[key] = (value, "command line");
        }

        ConfigurationBuilder builder = new();
        builder.AddInMemoryCollection(store.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value.Value)));
        IConfiguration configuration = builder.Build();

        R2Options? storeOptions;
        try
        {
            storeOptions = R2Options.FromConfiguration(configuration);
        }
        catch (InvalidOperationException error)
        {
            throw new UsageException(error.Message);
        }

        if (storeOptions is null)
        {
            throw new UsageException(
                "No store settings: set Storage__Region, Storage__Bucket, Storage__AccessKeyId, " +
                "Storage__SecretAccessKey and Storage__MediaBaseUrl in the environment or an --env-file");
        }

        string? evidenceBucket = NonEmpty(configuration["Moderation:EvidenceBucket"]);
        string? distributionId = NonEmpty(configuration["Moderation:CloudFront:DistributionId"]);
        if (distributionId is not null && storeOptions.Endpoint.Length > 0)
        {
            // As ModerationSetup refuses it: the purge is signed with the store's keys, which only AWS S3 shares.
            throw new UsageException(
                "Moderation:CloudFront:DistributionId needs the store on AWS S3 (Storage:Region without Storage:Endpoint)");
        }

        if (!options.TryGetValue("--site-origin", out string? siteOrigin))
        {
            throw new UsageException("--site-origin is required: the site whose browsers upload, e.g. https://reelshelf.app");
        }

        siteOrigin = Origin("--site-origin", siteOrigin);
        string otherOrigin = Origin("--other-origin", options.GetValueOrDefault("--other-origin", DefaultOtherOrigin));
        if (string.Equals(siteOrigin, otherOrigin, StringComparison.OrdinalIgnoreCase))
        {
            throw new UsageException("--other-origin must differ from --site-origin");
        }

        (string? connectHost, int? connectPort) = ConnectHost(options.GetValueOrDefault("--media-connect-host"));

        TimeSpan purgeWaitLimit = DefaultPurgeWaitLimit;
        if (options.TryGetValue("--purge-wait-seconds", out string? seconds))
        {
            if (!int.TryParse(seconds, out int value) || value is < 1 or > 3600)
            {
                throw new UsageException("--purge-wait-seconds must be a whole number of seconds from 1 to 3600");
            }

            purgeWaitLimit = TimeSpan.FromSeconds(value);
        }

        List<SettingSource> sources = StoreKeys
            .Where(key => store.TryGetValue(key, out (string Value, string Source) found) && found.Value.Trim().Length > 0)
            .Select(key => new SettingSource(key, store[key].Source))
            .ToList();

        return new CheckSettings(storeOptions, evidenceBucket, distributionId, siteOrigin, otherOrigin, connectHost,
            connectPort, flags.Contains("--skip-cdn"), !flags.Contains("--no-purge-wait"), purgeWaitLimit, sources,
            ignored);
    }

    /// <summary>
    /// KEY=VALUE lines as <c>docker --env-file</c> reads them: blank lines and <c>#</c> comments are skipped, the value
    /// is everything after the first <c>=</c> taken literally (no quotes removed, nothing expanded), and an
    /// <c>export </c> prefix is allowed. A line that isn't one of those is refused by its number alone, so a value is
    /// never echoed.
    /// </summary>
    public static IEnumerable<(string Name, string Value)> ParseEnvFile(IReadOnlyList<string> lines, string path)
    {
        for (int index = 0; index < lines.Count; index++)
        {
            string line = lines[index].TrimEnd('\r');
            string trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            if (trimmed.StartsWith("export ", StringComparison.Ordinal))
            {
                trimmed = trimmed["export ".Length..].TrimStart();
            }

            int equals = trimmed.IndexOf('=');
            string name = equals > 0 ? trimmed[..equals] : "";
            if (name.Length == 0 || !name.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '.' or ':'))
            {
                throw new UsageException($"--env-file {Path.GetFileName(path)}: line {index + 1} is not KEY=VALUE");
            }

            yield return (name, trimmed[(equals + 1)..]);
        }
    }

    /// <summary>The configuration key an environment variable sets, when it is one the check reads.</summary>
    private static string? StoreKey(string name)
    {
        string key = name.Replace("__", ":", StringComparison.Ordinal);
        return StoreKeys.FirstOrDefault(known => string.Equals(known, key, StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, string> ParseArguments(IReadOnlyList<string> args, out HashSet<string> flags,
        out Dictionary<string, string> storeArguments)
    {
        Dictionary<string, string> options = new(StringComparer.Ordinal);
        flags = new HashSet<string>(StringComparer.Ordinal);
        storeArguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < args.Count; index++)
        {
            string argument = args[index];
            string name = argument;
            string? value = null;
            int equals = argument.IndexOf('=');
            if (argument.StartsWith("--", StringComparison.Ordinal) && equals > 0)
            {
                name = argument[..equals];
                value = argument[(equals + 1)..];
            }

            if (name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("access-key", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("accesskey", StringComparison.OrdinalIgnoreCase))
            {
                throw new UsageException(
                    $"{name}: the storage key is never taken from the command line, where it would land in the " +
                    "shell's history; put it in the environment or an --env-file");
            }

            if (Flags.Contains(name))
            {
                if (value is not null)
                {
                    throw new UsageException($"{name} takes no value");
                }

                flags.Add(name);
                continue;
            }

            bool isStore = StoreOptions.TryGetValue(name, out string? storeKey);
            if (!isStore && !ValueOptions.Contains(name))
            {
                throw new UsageException($"Unknown argument {name}; see --help");
            }

            if (value is null)
            {
                if (index + 1 >= args.Count)
                {
                    throw new UsageException($"{name} needs a value");
                }

                value = args[++index];
            }

            if (isStore)
            {
                storeArguments[storeKey!] = value;
            }
            else
            {
                options[name] = value;
            }
        }

        return options;
    }

    private static string Origin(string option, string value)
    {
        string trimmed = value.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme is not ("https" or "http") || uri.PathAndQuery != "/" || uri.UserInfo.Length > 0)
        {
            throw new UsageException($"{option} must be an origin such as https://reelshelf.app");
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }

    private static (string? Host, int? Port) ConnectHost(string? value)
    {
        if (value is null)
        {
            return (null, null);
        }

        string trimmed = value.Trim();
        if (!Uri.TryCreate($"https://{trimmed}", UriKind.Absolute, out Uri? uri) || uri.PathAndQuery != "/" ||
            uri.UserInfo.Length > 0 || uri.Host.Length == 0)
        {
            throw new UsageException("--media-connect-host must be a host name, optionally with :port");
        }

        return (uri.Host, uri.IsDefaultPort ? null : uri.Port);
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
