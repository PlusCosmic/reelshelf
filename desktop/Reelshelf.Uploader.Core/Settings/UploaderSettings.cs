using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reelshelf.Uploader.Core.Settings;

/// <summary>Where the uploader keeps its files: <c>%LOCALAPPDATA%\Reelshelf\Uploader</c> on Windows.</summary>
public sealed class UploaderPaths(string root)
{
    public static UploaderPaths Default() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Reelshelf", "Uploader"));

    public string Root { get; } = root;

    public string Settings => Path.Combine(Root, "settings.json");

    public string Ledger => Path.Combine(Root, "ledger.db");

    public string Logs => Path.Combine(Root, "logs");
}

/// <summary>What the uploader does while a full-screen game is running (S5).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FullScreenMode>))]
public enum FullScreenMode
{
    /// <summary>Clips wait until the game is closed. The default.</summary>
    Wait,

    /// <summary>Clips upload at the speed limit, or <see cref="UploaderSettings.SlowUploadMbps"/> without one.</summary>
    UploadSlowly
}

/// <summary>
/// The uploader's settings (<c>settings.json</c>): the recordings folder, which book each of its folders goes on, the
/// default book, and the options in S5. Everything stays on the device.
/// </summary>
public sealed class UploaderSettings
{
    /// <summary>"Upload slowly" without a speed limit.</summary>
    public const int SlowUploadMbps = 5;

    /// <summary>The speed limits S5 offers, besides none.</summary>
    public static readonly IReadOnlyList<int> SpeedLimits = [20, 10, 5, 2];

    public string? RecordingsFolder { get; set; }

    /// <summary>
    /// The book chosen for each folder below the recordings folder, by its name ("" for the recordings folder itself),
    /// ignoring case. A choice here wins over matching by name.
    /// </summary>
    public Dictionary<string, Guid> FolderBooks { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Where clips that match no book go; null asks each time (S3).</summary>
    public Guid? DefaultBookId { get; set; }

    public FullScreenMode FullScreen { get; set; } = FullScreenMode.Wait;

    /// <summary>Megabits a second for every upload; null for no limit.</summary>
    public int? SpeedLimitMbps { get; set; }

    /// <summary>Whether N1 ("clips are on the shelf") is shown.</summary>
    public bool NotifyShelved { get; set; } = true;

    /// <summary>The name the device signed in with, shown in S5.</summary>
    public string? DeviceName { get; set; }

    /// <summary>
    /// The book a folder's clips go on when it has no saved choice: the default book, else none (the clip waits for
    /// one).
    /// </summary>
    public Guid? SavedBookFor(string folder) =>
        FolderBooks.TryGetValue(folder, out Guid book) ? book : null;
}

/// <summary>Reads and writes <see cref="UploaderSettings"/>, writing through a temporary file so a crash never leaves half a file.</summary>
public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public UploaderSettings Load()
    {
        try
        {
            if (!File.Exists(path))
            {
                return new UploaderSettings();
            }

            UploaderSettings? settings = JsonSerializer.Deserialize<UploaderSettings>(File.ReadAllText(path), Json);
            if (settings is null)
            {
                return new UploaderSettings();
            }

            // JSON gives a case-sensitive dictionary; folder names are matched ignoring case.
            settings.FolderBooks = new Dictionary<string, Guid>(settings.FolderBooks, StringComparer.OrdinalIgnoreCase);
            return settings;
        }
        catch (JsonException)
        {
            return new UploaderSettings();
        }
    }

    public void Save(UploaderSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Json));
        File.Move(temporary, path, overwrite: true);
    }
}
