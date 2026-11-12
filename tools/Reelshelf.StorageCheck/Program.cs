using System.Collections;
using System.Runtime.InteropServices;

namespace Reelshelf.StorageCheck;

public static class StorageCheckProgram
{
    public static async Task<int> Main(string[] args)
    {
        CheckSettings? settings;
        try
        {
            settings = CheckSettings.Parse(args, EnvironmentVariables(), path => File.ReadAllLines(path));
        }
        catch (UsageException error)
        {
            await Console.Error.WriteLineAsync($"storage check: {error.Message}");
            await Console.Error.WriteLineAsync("Run with --help for the options.");
            return StorageCheckRunner.ExitUsage;
        }

        if (settings is null)
        {
            Console.WriteLine(CheckSettings.Usage);
            return StorageCheckRunner.ExitOk;
        }

        using CancellationTokenSource interrupt = new();
        int interrupts = 0;
        void OnSignal(PosixSignalContext context)
        {
            // Never let the signal end the process: cleanup must run, and it has its own time limit.
            context.Cancel = true;
            if (Interlocked.Increment(ref interrupts) == 1)
            {
                Console.WriteLine();
                Console.WriteLine($"{context.Signal}: stopping the checks, then cleaning up. Signals are ignored until cleanup is done.");
                interrupt.Cancel();
            }
            else
            {
                Console.WriteLine("Still cleaning up; it stops by itself within 3 minutes.");
            }
        }

        using PosixSignalRegistration sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, OnSignal);
        using PosixSignalRegistration sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnSignal);

        using StorageCheckRunner runner = new(settings, CheckServices.Real(settings), Console.Out);
        return await runner.RunAsync(interrupt.Token);
    }

    private static Dictionary<string, string> EnvironmentVariables()
    {
        Dictionary<string, string> variables = new(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string name && entry.Value is string value)
            {
                variables[name] = value;
            }
        }

        return variables;
    }
}
