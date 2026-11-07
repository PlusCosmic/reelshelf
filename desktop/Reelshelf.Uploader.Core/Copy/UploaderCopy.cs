using System.Globalization;

namespace Reelshelf.Uploader.Core.Copy;

/// <summary>
/// Every word the uploader shows, worded as in reelshelf-docs/desktop-uploader/screens.html and following
/// frontend/src/copy/README.md: British English, sentence case, contractions, straight quotes, "·" between facts and
/// "…" for anything still happening or a button that opens another step. Errors say what didn't happen, then what to
/// do. Counts go through <see cref="Plural"/>. Nothing the app shows is written anywhere else, in code or XAML.
/// </summary>
public static class UploaderCopy
{
    public const string AppName = "Reelshelf";

    /// <summary>"1 clip", "3 clips": the count followed by the word that fits it.</summary>
    public static string Plural(int count, string one, string many) =>
        $"{count.ToString("N0", CultureInfo.GetCultureInfo("en-GB"))} {(count == 1 ? one : many)}";

    public static string Clips(int count) => Plural(count, "clip", "clips");

    /// <summary>"2:14", a clip's length.</summary>
    public static string Length(double seconds)
    {
        int whole = (int)Math.Round(seconds);
        return whole >= 3600
            ? $"{whole / 3600}:{whole / 60 % 60:00}:{whole % 60:00}"
            : $"{whole / 60}:{whole % 60:00}";
    }

    /// <summary>"74 MB", a file's size.</summary>
    public static string Size(long bytes)
    {
        const double mb = 1024 * 1024;
        return bytes >= 1024 * mb
            ? $"{(bytes / (1024 * mb)).ToString("0.0", CultureInfo.InvariantCulture)} GB"
            : $"{Math.Max(1, (int)Math.Round(bytes / mb))} MB";
    }

    /// <summary>"21:47", a time today.</summary>
    public static string Time(DateTimeOffset localTime) => localTime.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>"6 Nov 2026".</summary>
    public static string Date(DateTimeOffset localTime) =>
        localTime.ToString("d MMM yyyy", CultureInfo.GetCultureInfo("en-GB"));

    /// <summary>A folder as the screens name it, with Windows' separator: "Marvel Rivals\".</summary>
    public static string Folder(string name) => name.Length == 0 ? name : $"{name}\\";

    /// <summary>"in 2 minutes", "in 30 seconds".</summary>
    public static string In(TimeSpan wait) =>
        wait.TotalSeconds < 60
            ? $"in {Plural(Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)), "second", "seconds")}"
            : $"in {Plural((int)Math.Ceiling(wait.TotalMinutes), "minute", "minutes")}";

    /// <summary>S1: signing in.</summary>
    public static class SignIn
    {
        public const string Title = "Put your clips on the shelf while you play";
        public const string Intro =
            "Sign in once and Reelshelf puts new clips from your recordings folder on their books in the background.";

        public static string Waiting(string site) => $"Waiting for you to approve this code at {site}…";

        public const string BrowserOpened = "Your browser should have opened it.";
        public const string CopyCode = "Copy code";
        public const string OpenAgain = "Open the page again";
        public const string Starting = "Getting a code…";
        public const string Expired = "That code expired before it was approved. Get a new code to try again.";
        public const string GetNewCode = "Get a new code";
        public const string Denied = "Sign-in was cancelled on reelshelf.app, so this computer isn't signed in.";
        public const string TryAgain = "Try again";
        public const string Unreachable =
            "Couldn't reach Reelshelf, so this computer isn't signed in. Check your connection and try again.";
    }

    /// <summary>S2: choosing the recordings folder.</summary>
    public static class Setup
    {
        public const string Title = "Where does your recorder save clips?";
        public const string FolderLabel = "Recordings folder";
        public const string ChangeFolder = "Change…";
        public const string ChooseFolder = "Choose…";
        public const string FolderPickerTitle = "Choose your recordings folder";
        public const string MatchedHeading = "We matched these folders to your books";
        public const string NoBook = "No book";

        /// <summary>Between a folder and its book.</summary>
        public const string MatchArrow = "→";

        public const string ChooseBook = "Choose…";
        public const string NoSubfolders = "Clips saved straight into this folder go to the book below.";
        public const string DefaultBookLabel = "Clips that don't match a book go to";
        public const string AskEachTime = "Ask me each time";
        public const string Baseline = "Clips already in this folder stay where they are. Only new clips are uploaded.";
        public const string EmptyShelf = "Your shelf is empty. Add a game on reelshelf.app first.";
        public const string AddGame = "Add a game on reelshelf.app";
        public const string AlsoStartWithWindows = "Also start with Windows";
        public const string StartWatching = "Start watching";
        public const string BooksFailed = "Your books didn't load. Check your connection and try again.";
        public const string TryAgain = "Try again";
    }

    /// <summary>T1–T8 and the tray menu.</summary>
    public static class Tray
    {
        public static class Watching
        {
            public const string Title = "Watching for clips";

            public static string Detail(string folder, int shelvedToday) =>
                $"{folder} · {Clips(shelvedToday)} on the shelf today";

            public const string Action = "Show recent uploads";
        }

        public static class Uploading
        {
            public static string Title(int clips) => $"Uploading {Clips(clips)}…";

            public static string Detail(string title, int percent) => $"{title} · {percent}%";

            public const string Action = "Show recent uploads";
        }

        public static class GameRunning
        {
            public const string Title = "Waiting for you to finish playing…";

            public static string Detail(int waiting) => $"{Clips(waiting)} will upload when you leave the game";

            public const string Action = "Upload now";
        }

        public static class Paused
        {
            public static string Until(DateTimeOffset localTime) => $"Paused until {Time(localTime)}";

            public const string UntilTomorrow = "Paused until tomorrow";
            public const string UntilResumed = "Paused";
            public const string Detail = "New clips will upload when you resume";
            public const string Action = "Resume";
        }

        public static class NeedsBook
        {
            public static string Title(string folder) => $"{folder} needs a book";

            /// <summary>Clips saved straight into the recordings folder, with no default book.</summary>
            public const string RootTitle = "Your recordings folder needs a book";

            public static string Detail(int waiting) =>
                waiting == 1 ? "1 clip is waiting" : $"{Clips(waiting)} are waiting";

            public const string Action = "Choose a book…";
        }

        public static class LibraryFull
        {
            public const string Title = "Your library is full";

            public static string Detail(int? clipLimit, int waiting) => clipLimit is { } limit
                ? $"Your plan keeps {Clips(limit)} · {Clips(waiting)} waiting"
                : $"{Clips(waiting)} waiting";

            public const string MonthlyTitle = "You've added a lot of clips this month";

            public static string MonthlyDetail(DateTimeOffset? uploadsFrom, int waiting) => uploadsFrom is { } from
                ? $"New clips upload from {Date(from)} · {Clips(waiting)} waiting"
                : $"{Clips(waiting)} waiting";

            public const string Action = "See plans";
        }

        public static class Unreachable
        {
            public const string Title = "Couldn't reach Reelshelf";

            public static string Detail(TimeSpan retryIn, int waiting) =>
                $"Trying again {In(retryIn)} · {Clips(waiting)} waiting";

            public const string Action = "Try now";
        }

        public static class UpdateRequired
        {
            public const string Title = "Update Reelshelf";
            public const string Detail = "This version doesn't work with Reelshelf any more";
            public const string Action = "Open the Store";
        }

        public const string OpenLibrary = "Open your library";
        public const string PauseFor = "Pause for…";
        public const string PauseHour = "1 hour";
        public const string PauseTomorrow = "Until tomorrow";
        public const string PauseUntilResumed = "Until I resume";
        public const string Settings = "Settings";
        public const string Quit = "Quit";

        public static string QuitWhileUploading(int uploads) =>
            $"Quit and stop {Plural(uploads, "upload", "uploads")}? They'll carry on next time.";

        public const string QuitConfirm = "Quit";
        public const string Cancel = "Cancel";
    }

    /// <summary>S3: a folder with no book.</summary>
    public static class BookPicker
    {
        public const string WindowTitle = "Choose a book";

        public static string Heading(string folder) => $"Clips from {folder} go on";

        public const string RootHeading = "Clips saved straight into your recordings folder go on";
        public const string AddGame = "Add a game on reelshelf.app";
        public const string UseBook = "Use this book";
    }

    /// <summary>S4: recent uploads.</summary>
    public static class Recent
    {
        public const string WindowTitle = "Recent uploads";
        public const string Waiting = "Waiting";

        public static string Uploading(int percent) => $"Uploading… {percent}%";

        public const string OnShelf = "On the shelf";
        public const string Duplicate = "Already on the shelf";
        public const string TooLong = "Too long for your plan";
        public const string RetryingSoon = "Didn't upload, trying again soon";
        public const string Failed = "Didn't upload";
        public const string TryAgain = "Try again";
        public const string NeedsBook = "Waiting for a book";
        public const string LibraryFull = "Waiting for room in your library";
        public const string NoBook = "No book";

        public static string Meta(string book, double? lengthSeconds, long size, DateTimeOffset? shelvedAt)
        {
            List<string> facts = [book];
            if (lengthSeconds is { } length)
            {
                facts.Add(Length(length));
            }

            facts.Add(Size(size));
            if (shelvedAt is { } at)
            {
                facts.Add(Time(at));
            }

            return string.Join(" · ", facts);
        }

        public const string Empty = "Nothing yet. New clips from your recordings folder show up here.";
        public const string OpenFolder = "Open the recordings folder";
        public const string OpenLibrary = "Open your library";
    }

    /// <summary>S5: settings.</summary>
    public static class Settings
    {
        public const string WindowTitle = "Reelshelf settings";
        public const string FolderLabel = "Recordings folder";

        public static string FolderDetail(string path, int matched) =>
            $"{path} · {Plural(matched, "folder", "folders")} matched";

        public const string Change = "Change…";
        public const string DefaultBookLabel = "Clips that don't match a book";
        public const string ChangeDefault = "Change";
        public const string AskEachTime = "Ask me each time";
        public const string FullScreenLabel = "While a game is full screen";
        public const string FullScreenWait = "Uploads wait until you leave the game";
        public const string FullScreenSlow = "Uploads carry on at the speed limit, or 5 Mbit/s without one";
        public const string Wait = "Wait";
        public const string UploadSlowly = "Upload slowly";
        public const string SpeedLabel = "Upload speed limit";
        public const string SpeedDetail = "Applies to every upload";
        public const string NoLimit = "No limit";

        public static string Mbps(int mbps) => $"{mbps} Mbit/s";

        public const string StartWithWindows = "Start with Windows";
        public const string StartWithWindowsBlocked = "Windows has turned this off. Turn it on in Task Manager's startup apps.";
        public const string NotifyShelved = "Tell me when clips are on the shelf";
        public const string SignedIn = "Signed in";

        public static string DeviceName(string name) => $"This PC is \"{name}\" on reelshelf.app";

        public const string SignOut = "Sign out";

        public static string Version(string version) => $"Version {version}";

        public const string OpenLogFolder = "Open log folder";
    }

    /// <summary>N1–N5.</summary>
    public static class Notifications
    {
        public static string ShelvedTitle(int clips) =>
            clips == 1 ? "1 clip is on the shelf" : $"{Clips(clips)} are on the shelf";

        /// <summary>"Apex Legends (2), Valorant".</summary>
        public static string ShelvedBooks(IEnumerable<(string Book, int Clips)> books) =>
            string.Join(", ", books.Select(book => book.Clips > 1 ? $"{book.Book} ({book.Clips})" : book.Book));

        public const string OpenLibrary = "Open your library";

        public static string NeedsBookTitle(string folderName) => $"Which book do {folderName} clips go on?";

        public const string NeedsBookRootTitle = "Which book do clips in your recordings folder go on?";

        public static string NeedsBookDetail(int waiting, string folder) =>
            waiting == 1 ? $"1 clip is waiting in {folder}" : $"{Clips(waiting)} are waiting in {folder}";

        public static string NeedsBookRootDetail(int waiting) =>
            waiting == 1 ? "1 clip is waiting" : $"{Clips(waiting)} are waiting";

        public const string ChooseBook = "Choose a book";
        public const string NotNow = "Not now";

        public const string LibraryFullTitle = "Your library is full";

        public static string LibraryFullDetail(int? clipLimit) => clipLimit is { } limit
            ? $"Your plan keeps {Clips(limit)}, so new clips are waiting. They'll upload once there's room."
            : "New clips are waiting. They'll upload once there's room.";

        public static string MonthlyLimitDetail(DateTimeOffset? uploadsFrom) => uploadsFrom is { } from
            ? $"You've added a lot of clips this month, so new clips are waiting. They'll upload from {Date(from)}."
            : "You've added a lot of clips this month, so new clips are waiting. They'll upload once there's room.";

        public const string SeePlans = "See plans";
        public const string UpdateTitle = "Update Reelshelf to keep going";
        public const string UpdateDetail =
            "This version doesn't work with Reelshelf any more, so clips aren't uploading. Get the latest from the Store.";
        public const string OpenStore = "Open the Store";
        public const string SignedOutTitle = "This computer was signed out, so clips aren't uploading.";
        public const string SignedOutDetail = "Sign in again to carry on.";
        public const string SignIn = "Sign in";
    }
}
