using System.Net;
using Reelshelf.Uploader.ApiClient;
using Reelshelf.Uploader.ApiClient.Models;
using Reelshelf.Uploader.Core.Api;
using Reelshelf.Uploader.Core.Files;
using Reelshelf.Uploader.Core.Ledger;
using Reelshelf.Uploader.Core.Matching;
using Reelshelf.Uploader.Core.Platform;
using Reelshelf.Uploader.Core.Settings;
using Reelshelf.Uploader.Core.State;
using Reelshelf.Uploader.Core.Uploads;

namespace Reelshelf.Uploader.Core;

/// <summary>
/// The uploader's running part: watches the recordings folder, keeps the account's books and plan, uploads one clip at
/// a time, and works out the tray's <see cref="State"/> and the notifications. The shell (the Windows app) renders
/// what it says and calls its methods; it works nothing out itself.
/// </summary>
public sealed class UploaderEngine : IDisposable
{
    public static readonly TimeSpan RescanEvery = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan BooksEvery = TimeSpan.FromHours(1);
    public static readonly TimeSpan PlanFullRecheck = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan ShelvedNotificationGap = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan GateCheckEvery = TimeSpan.FromSeconds(2);

    private readonly UploaderApi _api;
    private readonly UploadLedger _ledger;
    private readonly SettingsStore _settingsStore;
    private readonly ICredentialStore _credentials;
    private readonly IGameActivity _game;
    private readonly INotifier _notifier;
    private readonly IClock _clock;
    private readonly FolderMonitor _monitor;
    private readonly UploadPipeline _pipeline;
    private readonly MultipartUploader _uploader;
    private readonly TokenBucket _bucket;
    private readonly EngineGate _gate;
    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _wake = new(0);
    private readonly Dictionary<string, int> _askedFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _shelvedSinceNotice = new(StringComparer.Ordinal);

    private FolderWatcher? _watcher;
    private volatile bool _rescanRequested = true;
    private DateTimeOffset _lastRescan = DateTimeOffset.MinValue;
    private DateTimeOffset _lastBooks = DateTimeOffset.MinValue;
    private DateTimeOffset _lastPlanCheck = DateTimeOffset.MinValue;
    private DateTimeOffset _lastShelvedNotice = DateTimeOffset.MinValue;
    private bool _updateRequired;
    private bool _updateNotified;
    private bool _signedOut;
    private bool _libraryFullNotified;
    private LibraryFullInfo? _libraryFull;
    private DateTimeOffset? _unreachableRetryAt;
    private PauseInfo? _paused;
    private bool _uploadNow;
    private (LedgerEntry Entry, int Percent)? _current;

    public UploaderEngine(
        UploaderApi api,
        HttpClient storage,
        UploadLedger ledger,
        SettingsStore settingsStore,
        ICredentialStore credentials,
        IGameActivity game,
        INotifier notifier,
        IClock clock,
        Func<string, bool>? canOpen = null)
    {
        _api = api;
        _ledger = ledger;
        _settingsStore = settingsStore;
        _credentials = credentials;
        _game = game;
        _notifier = notifier;
        _clock = clock;
        Settings = settingsStore.Load();
        _monitor = new FolderMonitor(ledger, clock, canOpen);
        _bucket = new TokenBucket(clock);
        _gate = new EngineGate(this);
        _uploader = new MultipartUploader(api, storage, ledger, clock, _bucket, _gate);
        _uploader.Progress += OnProgress;
        _pipeline = new UploadPipeline(api, ledger, _uploader, clock);
        api.Handler.SignedOut += (_, _) => OnSignedOut();
        api.Handler.UpdateRequired += (_, _) => OnUpdateRequired();
        _signedOut = string.IsNullOrEmpty(credentials.ReadToken());
    }

    public UploaderSettings Settings { get; }

    public IReadOnlyList<Book> Books { get; private set; } = [];

    public PlanLimits? Plan { get; private set; }

    public UploaderState State { get; private set; } =
        UploaderState.From(new StateInputs(), DateTimeOffset.UtcNow);

    public bool IsSignedIn => !_signedOut;

    public bool IsSetUp => !string.IsNullOrEmpty(Settings.RecordingsFolder);

    /// <summary>The tray's state changed.</summary>
    public event EventHandler<UploaderState>? StateChanged;

    /// <summary>The API refused the token; the shell shows S1.</summary>
    public event EventHandler? SignedOut;

    /// <summary>A folder needs a book chosen (S3).</summary>
    public event EventHandler<string>? BookNeeded;

    public event EventHandler? BooksChanged;

    /// <summary>A clip's upload moved on, for S4.</summary>
    public event EventHandler? LedgerChanged;

    public string? Root => _monitor.Root;

    // ---- Setup and settings (S2, S3, S5) ----

    /// <summary>
    /// Watches a new recordings folder. Files already in it are baselined; the ledger is kept, so a folder used before
    /// doesn't upload twice.
    /// </summary>
    public void SetRecordingsFolder(string path)
    {
        Settings.RecordingsFolder = Path.GetFullPath(path);
        SaveSettings();
        _monitor.Use(Settings.RecordingsFolder, baseline: true);
        StartWatcher();
        _rescanRequested = true;
        Wake();
    }

    /// <summary>Saves the book for a folder; its waiting clips go on it at once.</summary>
    public void ChooseBookForFolder(string folder, Guid bookId)
    {
        Settings.FolderBooks[folder] = bookId;
        SaveSettings();
        _askedFolders.Remove(folder);
        if (_monitor.Root is { } root)
        {
            _ledger.AssignBook(root, folder, bookId, _clock.UtcNow);
        }

        Changed();
    }

    public void SetDefaultBook(Guid? bookId)
    {
        Settings.DefaultBookId = bookId;
        SaveSettings();
        _monitor.Rematch(Books, Settings);
        Changed();
    }

    public void UpdateSettings(Action<UploaderSettings> change)
    {
        change(Settings);
        SaveSettings();
        Changed();
    }

    /// <summary>The folders below the recordings folder with videos in them, with the book each goes on (S2, S5).</summary>
    public List<(string Folder, Book? Book)> FolderMatches(string? root = null)
    {
        root ??= Settings.RecordingsFolder;
        if (root is null)
        {
            return [];
        }

        return FolderScanner.FoldersWithVideos(root)
            .Select(folder =>
            {
                BookMatch match = BookMatcher.Resolve(folder, Books, Settings.SavedBookFor(folder), null);
                return (folder, Books.FirstOrDefault(book => book.Id == match.CategoryId));
            })
            .ToList();
    }

    // ---- The tray's actions ----

    public void Pause(PauseLength length)
    {
        DateTimeOffset now = _clock.UtcNow;
        DateTimeOffset? until = length switch
        {
            PauseLength.OneHour => now.AddHours(1),
            PauseLength.UntilTomorrow => TomorrowMorning(now),
            _ => null
        };
        lock (_lock)
        {
            _paused = new PauseInfo(length, until);
        }

        Changed();
    }

    public void Resume()
    {
        lock (_lock)
        {
            _paused = null;
        }

        Changed();
        Wake();
    }

    /// <summary>T3's "Upload now": uploads carry on while this game is running.</summary>
    public void UploadNow()
    {
        _uploadNow = true;
        Changed();
        Wake();
    }

    /// <summary>T7's "Try now", and S4's "Try again" for a file that failed.</summary>
    public void TryNow(LedgerEntry? entry = null)
    {
        if (_monitor.Root is { } root)
        {
            foreach (LedgerEntry waiting in _ledger.InState(root, LedgerState.Waiting, LedgerState.Uploading))
            {
                waiting.NextAttemptAt = null;
                _ledger.Save(waiting);
            }
        }

        if (entry is { State: LedgerState.Failed })
        {
            entry.State = entry.ClipId is null ? LedgerState.Waiting : LedgerState.Uploading;
            entry.Attempts = UploadPipeline.MaxAttempts - 1;
            entry.NextAttemptAt = null;
            entry.UpdatedAt = _clock.UtcNow;
            _ledger.Save(entry);
        }

        _unreachableRetryAt = null;
        Changed();
        Wake();
    }

    public List<LedgerEntry> Recent() => _ledger.Recent();

    /// <summary>Clips uploading or waiting for their turn, which Quit would stop.</summary>
    public int UploadsInProgress => _monitor.Root is { } root
        ? _ledger.Count(root, LedgerState.Uploading, LedgerState.Waiting)
        : 0;

    // ---- Signing in and out ----

    /// <summary>The app signed in (S1): uploads carry on.</summary>
    public void OnSignedIn()
    {
        _signedOut = false;
        _lastBooks = DateTimeOffset.MinValue;
        _lastPlanCheck = DateTimeOffset.MinValue;
        Changed();
        Wake();
    }

    /// <summary>S5's Sign out: revokes this device's token and forgets it. The folder settings and ledger stay.</summary>
    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        await _api.SignOutAsync(cancellationToken);
        _credentials.DeleteToken();
        _signedOut = true;
        Changed();
    }

    private void OnSignedOut()
    {
        if (_signedOut)
        {
            return;
        }

        _signedOut = true;
        _credentials.DeleteToken();
        _notifier.SignedOut();
        SignedOut?.Invoke(this, EventArgs.Empty);
        Changed();
    }

    private void OnUpdateRequired()
    {
        _updateRequired = true;
        if (!_updateNotified)
        {
            _updateNotified = true;
            _notifier.UpdateRequired();
        }

        Changed();
    }

    // ---- Running ----

    /// <summary>
    /// Runs until cancelled: the folder's watcher, a settle check every 2 s, a rescan every 60 s (and after the watcher
    /// overflows), the books every hour, and uploads one at a time.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (Settings.RecordingsFolder is { } folder)
        {
            _monitor.Use(folder, baseline: false);
            _ledger.RetryFailedOnce(_monitor.Root!, UploadPipeline.MaxAttempts, _clock.UtcNow);
            StartWatcher();
        }

        await Task.WhenAll(MonitorLoopAsync(cancellationToken), UploadLoopAsync(cancellationToken));
    }

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await _clock.Delay(FileSettler.CheckEvery, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task UploadLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UploadOutcome? outcome;
            try
            {
                outcome = await UploadNextAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (outcome is null)
            {
                // Nothing to do: wait for a new clip, a change, or the next retry.
                await Task.WhenAny(_wake.WaitAsync(cancellationToken), _clock.Delay(GateCheckEvery, cancellationToken));
            }
        }
    }

    /// <summary>One pass of the monitor: books, the plan, rescans, settling and notifications.</summary>
    public async Task TickAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = _clock.UtcNow;
        if (_paused is { Until: { } until } && now >= until)
        {
            Resume();
        }

        if (!_game.IsGameRunning)
        {
            _uploadNow = false;
        }

        if (!_signedOut && !_updateRequired && now - _lastBooks >= BooksEvery)
        {
            await RefreshBooksAsync(cancellationToken);
        }

        if (!_signedOut && !_updateRequired && (Plan is null || (_libraryFull is not null && now - _lastPlanCheck >= PlanFullRecheck)))
        {
            await RefreshPlanAsync(cancellationToken);
        }

        if (_monitor.Root is not null)
        {
            if (_rescanRequested || now - _lastRescan >= RescanEvery)
            {
                _rescanRequested = false;
                _lastRescan = now;
                _monitor.Rescan();
            }

            List<LedgerEntry> added = _monitor.Settle(Books, Settings);
            if (added.Count > 0)
            {
                AskForBooks();
                LedgerChanged?.Invoke(this, EventArgs.Empty);
                Wake();
            }
        }

        NotifyShelved();
        Changed();
    }

    /// <summary>The account's books, at start, hourly and when S3 opens. A folder whose name now matches is matched.</summary>
    public async Task RefreshBooksAsync(CancellationToken cancellationToken)
    {
        ApiResult<List<Book>> books = await _api.GetBooksAsync(cancellationToken);
        if (!books.IsSuccess || books.Value is null)
        {
            return;
        }

        _lastBooks = _clock.UtcNow;
        Books = books.Value.OrderBy(book => book.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (_monitor.Rematch(Books, Settings) > 0)
        {
            Wake();
        }

        BooksChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task RefreshPlanAsync(CancellationToken cancellationToken)
    {
        ApiResult<AccountPlanResponse> plan = await _api.GetPlanAsync(cancellationToken);
        if (!plan.IsSuccess || plan.Value is null)
        {
            return;
        }

        _lastPlanCheck = _clock.UtcNow;
        Plan = PlanLimits.From(plan.Value);
        if (_libraryFull is not null && Plan.HasRoom)
        {
            _libraryFull = null;
            if (_monitor.Root is { } root)
            {
                _ledger.RequeuePlanFull(root, _clock.UtcNow);
            }

            Wake();
        }
    }

    /// <summary>
    /// Uploads the next file whose turn it is, if uploads may go now; null when there was nothing to do. A clip that
    /// hasn't started waits while a game is full screen ("Wait"); one already uploading stops between parts.
    /// </summary>
    public async Task<UploadOutcome?> UploadNextAsync(CancellationToken cancellationToken)
    {
        if (_monitor.Root is not { } root || _signedOut || _updateRequired || _libraryFull is not null ||
            _paused is not null || _gate.HoldsNewClips)
        {
            return null;
        }

        LedgerEntry? entry = _ledger.NextToUpload(root, _clock.UtcNow);
        if (entry is null)
        {
            return null;
        }

        if (Plan is null)
        {
            await RefreshPlanAsync(cancellationToken);
            if (Plan is null)
            {
                MarkUnreachable(root);
                return null;
            }
        }

        string fullPath = Path.Combine(root, entry.Path.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            // Deleted or moved before it could go; a rescan finds it again wherever it went.
            entry.State = LedgerState.Failed;
            entry.LastError = "The file is no longer in the recordings folder";
            entry.UpdatedAt = _clock.UtcNow;
            _ledger.Save(entry);
            return UploadOutcome.Failed;
        }

        lock (_lock)
        {
            _current = (entry, entry.Progress);
        }

        Changed();
        UploadOutcome outcome;
        try
        {
            outcome = await _pipeline.UploadAsync(entry, fullPath, Plan, cancellationToken);
        }
        finally
        {
            lock (_lock)
            {
                _current = null;
            }
        }

        HandleOutcome(root, entry, outcome);
        LedgerChanged?.Invoke(this, EventArgs.Empty);
        Changed();
        return outcome;
    }

    private void HandleOutcome(string root, LedgerEntry entry, UploadOutcome outcome)
    {
        switch (outcome)
        {
            case UploadOutcome.Shelved:
                _unreachableRetryAt = null;
                _libraryFullNotified = false;
                string book = Books.FirstOrDefault(b => b.Id == entry.CategoryId)?.Name ?? "";
                _shelvedSinceNotice[book] = _shelvedSinceNotice.GetValueOrDefault(book) + 1;
                break;
            case UploadOutcome.PlanFull:
                Plan ??= new PlanLimits(null, null, null, 0, 0);
                _libraryFull = new LibraryFullInfo(Plan.ClipLimit, Plan.MonthlyLimitReached, null);
                _lastPlanCheck = _clock.UtcNow;
                if (!_libraryFullNotified)
                {
                    _libraryFullNotified = true;
                    _notifier.LibraryFull(new LibraryFullReason(_libraryFull.ClipLimit, _libraryFull.MonthlyLimit,
                        _libraryFull.UploadsFrom));
                }

                break;
            case UploadOutcome.Unsupported:
                OnUpdateRequired();
                break;
            case UploadOutcome.UpdateRequired:
                OnUpdateRequired();
                break;
            case UploadOutcome.NeedsBook:
                AskForBooks();
                break;
            case UploadOutcome.RetryLater:
                MarkUnreachable(root);
                break;
            default:
                _unreachableRetryAt = null;
                break;
        }
    }

    private void MarkUnreachable(string root)
    {
        _unreachableRetryAt = _ledger.NextRetryAt(root) ?? _clock.UtcNow + UploadPipeline.Backoff(1);
    }

    // ---- Notifications ----

    /// <summary>N2 once per folder, the first time a clip there settles; again as its count changes.</summary>
    private void AskForBooks()
    {
        if (_monitor.Root is not { } root)
        {
            return;
        }

        foreach (IGrouping<string, LedgerEntry> folder in _ledger.InState(root, LedgerState.NeedsBook)
                     .GroupBy(entry => entry.Folder, StringComparer.OrdinalIgnoreCase))
        {
            int count = folder.Count();
            if (_askedFolders.TryGetValue(folder.Key, out int asked) && asked == count)
            {
                continue;
            }

            bool first = !_askedFolders.ContainsKey(folder.Key);
            _askedFolders[folder.Key] = count;
            _notifier.NeedsBook(folder.Key, count);
            if (first)
            {
                BookNeeded?.Invoke(this, folder.Key);
            }
        }
    }

    /// <summary>N1: grouped, at most one every 10 minutes, and never while a game is full screen.</summary>
    private void NotifyShelved()
    {
        DateTimeOffset now = _clock.UtcNow;
        if (_shelvedSinceNotice.Count == 0 || _game.IsGameRunning || now - _lastShelvedNotice < ShelvedNotificationGap)
        {
            return;
        }

        List<ShelvedBook> books = _shelvedSinceNotice
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new ShelvedBook(pair.Key, pair.Value))
            .ToList();
        _shelvedSinceNotice.Clear();
        _lastShelvedNotice = now;
        if (Settings.NotifyShelved)
        {
            _notifier.ClipsShelved(books);
        }
    }

    // ---- State ----

    public StateInputs Inputs()
    {
        string? root = _monitor.Root;
        (string Folder, int Waiting)? needsBook = null;
        int waiting = 0;
        int shelvedToday = 0;
        if (root is not null)
        {
            IGrouping<string, LedgerEntry>? folder = _ledger.InState(root, LedgerState.NeedsBook)
                .GroupBy(entry => entry.Folder, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (folder is not null)
            {
                needsBook = (folder.Key, folder.Count());
            }

            waiting = _ledger.Count(root, LedgerState.Waiting, LedgerState.Uploading, LedgerState.PlanFull);
            DateTimeOffset localMidnight = new(TimeZoneInfo.ConvertTime(_clock.UtcNow, TimeZoneInfo.Local).Date,
                TimeZoneInfo.Local.GetUtcOffset(_clock.UtcNow));
            shelvedToday = _ledger.ShelvedSince(root, localMidnight);
        }

        lock (_lock)
        {
            return new StateInputs
            {
                UpdateRequired = _updateRequired,
                LibraryFull = _libraryFull,
                UnreachableRetryAt = _unreachableRetryAt,
                NeedsBook = needsBook,
                Paused = _paused,
                GameHoldsUploads = _gate.HoldsNewClips,
                Waiting = waiting,
                Current = _current is var (entry, percent) ? (entry.Title, percent) : null,
                FolderName = UploaderState.ShortFolderName(Settings.RecordingsFolder),
                ShelvedToday = shelvedToday
            };
        }
    }

    private void Changed()
    {
        UploaderState state = UploaderState.From(Inputs(), _clock.UtcNow);
        if (state == State)
        {
            return;
        }

        State = state;
        StateChanged?.Invoke(this, state);
    }

    private void OnProgress(LedgerEntry entry, long sent, long total)
    {
        lock (_lock)
        {
            _current = (entry, total <= 0 ? 0 : (int)Math.Min(100, sent * 100 / total));
        }

        Changed();
        LedgerChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Wake()
    {
        if (_wake.CurrentCount == 0)
        {
            _wake.Release();
        }
    }

    private void StartWatcher()
    {
        _watcher?.Dispose();
        _watcher = null;
        if (_monitor.Root is not { } root || !Directory.Exists(root))
        {
            return;
        }

        _watcher = new FolderWatcher(root);
        _watcher.Dirty += (_, path) => _monitor.MarkDirty(path);
        _watcher.RescanNeeded += (_, _) => _rescanRequested = true;
        try
        {
            _watcher.Start();
        }
        catch (Exception error) when (error is IOException or ArgumentException or PlatformNotSupportedException)
        {
            // Without events the 60-second rescan still finds every clip.
            _watcher.Dispose();
            _watcher = null;
        }
    }

    private void SaveSettings() => _settingsStore.Save(Settings);

    private static DateTimeOffset TomorrowMorning(DateTimeOffset now)
    {
        DateTime local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local).Date.AddDays(1);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _wake.Dispose();
    }

    /// <summary>The engine's rule for when uploads go and how fast.</summary>
    private sealed class EngineGate(UploaderEngine engine) : IUploadGate
    {
        /// <summary>A full-screen game holds uploads ("Wait"), unless "Upload now" was chosen during it.</summary>
        public bool HoldsNewClips =>
            engine._game.IsGameRunning && engine.Settings.FullScreen == FullScreenMode.Wait && !engine._uploadNow;

        public async Task WaitUntilOpenAsync(CancellationToken cancellationToken)
        {
            while (HoldsNewClips || engine._paused is not null)
            {
                engine.Changed();
                await engine._clock.Delay(GateCheckEvery, cancellationToken);
                if (engine._paused is { Until: { } until } && engine._clock.UtcNow >= until)
                {
                    engine.Resume();
                }

                if (!engine._game.IsGameRunning)
                {
                    engine._uploadNow = false;
                }
            }
        }

        public long? BytesPerSecond
        {
            get
            {
                int? limit = engine.Settings.SpeedLimitMbps;
                bool slow = engine._game.IsGameRunning && engine.Settings.FullScreen == FullScreenMode.UploadSlowly &&
                            !engine._uploadNow;
                return TokenBucket.FromMbps(slow ? limit ?? UploaderSettings.SlowUploadMbps : limit);
            }
        }
    }
}
