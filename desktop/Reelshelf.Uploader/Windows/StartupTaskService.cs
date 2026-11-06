using Windows.ApplicationModel;

namespace Reelshelf.Uploader.Windows;

/// <summary>
/// Start with Windows, through the MSIX package's <c>windows.startupTask</c> (Package.appxmanifest, TaskId
/// <see cref="TaskId"/>). It is off until the person turns it on, and Windows may turn it off from Task Manager, after
/// which only the person can turn it on there.
/// </summary>
public static class StartupTaskService
{
    public const string TaskId = "ReelshelfUploaderStartup";

    public enum StartupState
    {
        Off,
        On,

        /// <summary>Turned off in Task Manager or by policy; the app can't turn it on.</summary>
        Blocked,

        /// <summary>Not running from the MSIX package (a development build), so there is no startup task.</summary>
        Unavailable
    }

    public static async Task<StartupState> GetAsync()
    {
        try
        {
            StartupTask task = await StartupTask.GetAsync(TaskId);
            return Map(task.State);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or
                                          System.Runtime.InteropServices.COMException)
        {
            return StartupState.Unavailable;
        }
    }

    public static async Task<StartupState> SetAsync(bool enabled)
    {
        try
        {
            StartupTask task = await StartupTask.GetAsync(TaskId);
            if (enabled)
            {
                return Map(await task.RequestEnableAsync());
            }

            task.Disable();
            return StartupState.Off;
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or
                                          System.Runtime.InteropServices.COMException)
        {
            return StartupState.Unavailable;
        }
    }

    private static StartupState Map(StartupTaskState state) => state switch
    {
        StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy => StartupState.On,
        StartupTaskState.DisabledByUser or StartupTaskState.DisabledByPolicy => StartupState.Blocked,
        _ => StartupState.Off
    };
}
