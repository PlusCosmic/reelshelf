using System.Net;
using System.Text.RegularExpressions;
using Amazon.Runtime;
using Reelshelf.Worker;

namespace Reelshelf.StorageCheck;

public enum Outcome
{
    Pass,
    Fail,
    Skip,

    /// <summary>Something worth knowing that is neither a pass nor a failure, such as what an emulator can't show.</summary>
    Note
}

public sealed record CheckResult(string Id, string Title, Outcome Outcome, string Detail);

/// <summary>A check's own verdict, with the reason that is printed for it.</summary>
public sealed class CheckFailedException(string message) : Exception(message);

/// <summary>
/// Prints each result as it comes and the summary at the end. Everything printed goes through <see cref="Redact"/>,
/// which removes the key and every presigned URL's credential and signature, so an error message quoting a URL
/// never leaks one.
/// </summary>
public sealed partial class CheckReport(TextWriter output, IEnumerable<string> secrets)
{
    private readonly List<string> _secrets = secrets.Where(secret => secret.Length >= 4).ToList();
    private readonly List<CheckResult> _results = [];
    private readonly Lock _lock = new();

    public IReadOnlyList<CheckResult> Results
    {
        get
        {
            lock (_lock)
            {
                return [.. _results];
            }
        }
    }

    public bool AnyFailed => Results.Any(result => result.Outcome == Outcome.Fail);

    public void Add(string id, string title, Outcome outcome, string detail)
    {
        CheckResult result = new(id, title, outcome, Redact(detail));
        lock (_lock)
        {
            _results.Add(result);
        }

        Line($"{Label(outcome),-4}  {id,-34} {title}");
        if (result.Detail.Length > 0)
        {
            Line($"      {result.Detail}");
        }
    }

    public void Line(string text = "")
    {
        lock (_lock)
        {
            output.WriteLine(Redact(text));
            output.Flush();
        }
    }

    public void Heading(string text)
    {
        Line();
        Line($"== {text}");
    }

    public void Summary()
    {
        List<CheckResult> results = [.. Results];
        Heading("Summary");
        Line($"{"Result",-6}  {"Check",-34} Detail");
        foreach (CheckResult result in results.Where(result => result.Outcome is Outcome.Pass or Outcome.Fail))
        {
            Line($"{Label(result.Outcome),-6}  {result.Id,-34} {Shorten(result.Detail)}");
        }

        List<CheckResult> skipped = results.Where(result => result.Outcome == Outcome.Skip).ToList();
        if (skipped.Count > 0)
        {
            Line();
            Line("Skipped:");
            foreach (CheckResult result in skipped)
            {
                Line($"  {result.Id}: {result.Detail}");
            }
        }

        List<CheckResult> notes = results.Where(result => result.Outcome == Outcome.Note).ToList();
        if (notes.Count > 0)
        {
            Line();
            Line("Notes:");
            foreach (CheckResult result in notes)
            {
                Line($"  {result.Id}: {result.Detail}");
            }
        }

        Line();
        Line($"{results.Count(result => result.Outcome == Outcome.Pass)} passed, " +
             $"{results.Count(result => result.Outcome == Outcome.Fail)} failed, {skipped.Count} skipped");
    }

    /// <summary>
    /// One line for an error: the HTTP status and S3/CloudFront error code wherever the chain holds them, and the
    /// message.
    /// </summary>
    public static string Describe(Exception error)
    {
        if (error is CheckFailedException failed)
        {
            return failed.Message;
        }

        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            if (current is AmazonServiceException service)
            {
                string prefix = current == error ? "" : $"{error.GetType().Name}: ";
                return $"{prefix}{(int)service.StatusCode} {service.StatusCode} {service.ErrorCode}: {service.Message}";
            }
        }

        string message = error switch
        {
            JobFailedException job => $"worker: {job.Message} (retryable: {job.Retryable})",
            _ => $"{error.GetType().Name}: {error.Message}"
        };
        Exception? inner = error.InnerException;
        while (inner is not null)
        {
            message += $" <- {inner.GetType().Name}: {inner.Message}";
            inner = inner.InnerException;
        }

        return message;
    }

    /// <summary>The status line and S3 error code of a raw response, for a failure's reason.</summary>
    public static string Status(HttpStatusCode status, string? errorCode) =>
        errorCode is null ? $"{(int)status} {status}" : $"{(int)status} {status} {errorCode}";

    public string Redact(string text)
    {
        string redacted = text;
        foreach (string secret in _secrets)
        {
            redacted = redacted.Replace(secret, "***", StringComparison.Ordinal);
        }

        redacted = SignaturePattern().Replace(redacted, "$1***");
        return CredentialPattern().Replace(redacted, "$1***");
    }

    private static string Label(Outcome outcome) => outcome switch
    {
        Outcome.Pass => "PASS",
        Outcome.Fail => "FAIL",
        Outcome.Skip => "SKIP",
        _ => "NOTE"
    };

    private static string Shorten(string detail) => detail.Length <= 100 ? detail : detail[..97] + "...";

    [GeneratedRegex("((?:X-Amz-Signature|Signature)=)[^&\\s\"']+", RegexOptions.IgnoreCase)]
    private static partial Regex SignaturePattern();

    [GeneratedRegex("((?:X-Amz-Credential|AWSAccessKeyId|X-Amz-Security-Token)=)[^&\\s\"']+", RegexOptions.IgnoreCase)]
    private static partial Regex CredentialPattern();
}
