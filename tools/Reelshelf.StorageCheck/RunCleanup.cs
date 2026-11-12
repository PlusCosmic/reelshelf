using Amazon.S3;
using Amazon.S3.Model;
using Reelshelf.Video.R2;

namespace Reelshelf.StorageCheck;

/// <summary>A bucket the run writes to, with the run's keys and folders in it.</summary>
public sealed record RunBucket(
    string Label,
    R2Storage Storage,
    IAmazonS3 Client,
    string BucketName,
    IReadOnlyList<string> Keys,
    IReadOnlyList<string> Prefixes,
    Func<string, bool> Owns);

/// <summary>What cleanup left behind, if anything, and what it couldn't look at.</summary>
public sealed record CleanupOutcome(
    bool Verified,
    IReadOnlyList<string> LeftObjects,
    IReadOnlyList<string> LeftUploads,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Unverifiable,
    int Removed);

/// <summary>
/// Removes everything of the run, whatever the checks did or didn't get to, and then lists again to prove it: open
/// multipart uploads on the run's keys are aborted (their parts are stored and billed, and nothing lists them as
/// objects), the run's exact keys deleted and its folders emptied, through <see cref="R2Storage"/>'s own deletes. It
/// never cancels on the run's token, and never touches a key <see cref="RunBucket.Owns"/> doesn't accept.
/// </summary>
public sealed class RunCleanup(IReadOnlyList<RunBucket> buckets)
{
    private const int Attempts = 3;

    public async Task<CleanupOutcome> RunAsync(Func<TimeSpan, Task> delay)
    {
        List<string> errors = [];
        List<string> unverifiable = [];
        int removed = 0;
        for (int attempt = 1; attempt <= Attempts; attempt++)
        {
            foreach (RunBucket bucket in buckets)
            {
                removed += await AbortUploadsAsync(bucket, errors, unverifiable);
                removed += await DeleteObjectsAsync(bucket, errors);
            }

            (List<string> objects, List<string> uploads) = await LeftoversAsync(errors, unverifiable);
            if (objects.Count == 0 && uploads.Count == 0 && errors.Count == 0)
            {
                return new CleanupOutcome(true, [], [], [], unverifiable.Distinct().ToList(), removed);
            }

            if (attempt == Attempts)
            {
                return new CleanupOutcome(false, objects, uploads, errors.Distinct().ToList(),
                    unverifiable.Distinct().ToList(), removed);
            }

            errors.Clear();
            unverifiable.Clear();
            await delay(TimeSpan.FromSeconds(2 * attempt));
        }

        throw new InvalidOperationException("unreachable");
    }

    private static async Task<int> AbortUploadsAsync(RunBucket bucket, List<string> errors, List<string> unverifiable)
    {
        int aborted = 0;
        foreach (string prefix in bucket.Keys.Concat(bucket.Prefixes))
        {
            List<MultipartUpload>? open = await OpenUploadsAsync(bucket, prefix, errors, unverifiable);
            foreach (MultipartUpload upload in open ?? [])
            {
                try
                {
                    await bucket.Client.AbortMultipartUploadAsync(new AbortMultipartUploadRequest
                    {
                        BucketName = bucket.BucketName, Key = upload.Key, UploadId = upload.UploadId
                    });
                    aborted++;
                }
                catch (AmazonS3Exception error) when (error.ErrorCode == "NoSuchUpload")
                {
                }
                catch (Exception error)
                {
                    errors.Add($"{bucket.Label}: abort the upload on {upload.Key}: {CheckReport.Describe(error)}");
                }
            }
        }

        return aborted;
    }

    private static async Task<int> DeleteObjectsAsync(RunBucket bucket, List<string> errors)
    {
        int deleted = 0;
        try
        {
            List<string> exact = [];
            foreach (string key in bucket.Keys)
            {
                // Only an object at exactly this key: a real key can't extend a fresh GUID, but the listing is by prefix.
                exact.AddRange((await bucket.Storage.ListAsync(key)).Select(item => item.Key).Where(found => found == key));
            }

            Require(bucket, exact);
            await bucket.Storage.DeleteObjectsAsync(exact);
            deleted += exact.Count;
        }
        catch (Exception error)
        {
            errors.Add($"{bucket.Label}: delete the run's keys: {CheckReport.Describe(error)}");
        }

        foreach (string prefix in bucket.Prefixes)
        {
            try
            {
                List<string> keys = (await bucket.Storage.ListAsync(prefix)).Select(item => item.Key).ToList();
                Require(bucket, keys);
                await bucket.Storage.DeletePrefixAsync(prefix);
                deleted += keys.Count;
            }
            catch (Exception error)
            {
                errors.Add($"{bucket.Label}: empty {prefix}: {CheckReport.Describe(error)}");
            }
        }

        return deleted;
    }

    private async Task<(List<string> Objects, List<string> Uploads)> LeftoversAsync(List<string> errors,
        List<string> unverifiable)
    {
        List<string> objects = [];
        List<string> uploads = [];
        foreach (RunBucket bucket in buckets)
        {
            foreach (string key in bucket.Keys)
            {
                try
                {
                    objects.AddRange((await bucket.Storage.ListAsync(key))
                        .Where(item => item.Key == key).Select(item => $"{bucket.Label}: {item.Key}"));
                }
                catch (Exception error)
                {
                    errors.Add($"{bucket.Label}: list {key}: {CheckReport.Describe(error)}");
                }
            }

            foreach (string prefix in bucket.Prefixes)
            {
                try
                {
                    objects.AddRange((await bucket.Storage.ListAsync(prefix)).Select(item => $"{bucket.Label}: {item.Key}"));
                }
                catch (Exception error)
                {
                    errors.Add($"{bucket.Label}: list {prefix}: {CheckReport.Describe(error)}");
                }
            }

            foreach (string prefix in bucket.Keys.Concat(bucket.Prefixes))
            {
                List<MultipartUpload>? open = await OpenUploadsAsync(bucket, prefix, errors, unverifiable);
                uploads.AddRange((open ?? []).Select(upload => $"{bucket.Label}: upload {upload.UploadId} on {upload.Key}"));
            }
        }

        return (objects, uploads);
    }

    /// <summary>
    /// The open uploads under <paramref name="prefix"/> that the run owns, or null when the key may not list them,
    /// as the API's key may not in the evidence bucket (its own uploads there abort themselves on failure).
    /// </summary>
    private static async Task<List<MultipartUpload>?> OpenUploadsAsync(RunBucket bucket, string prefix,
        List<string> errors, List<string> unverifiable)
    {
        List<MultipartUpload> open = [];
        string? keyMarker = null;
        string? uploadMarker = null;
        try
        {
            do
            {
                ListMultipartUploadsResponse page = await bucket.Client.ListMultipartUploadsAsync(
                    new ListMultipartUploadsRequest
                    {
                        BucketName = bucket.BucketName, Prefix = prefix, KeyMarker = keyMarker, UploadIdMarker = uploadMarker
                    });
                open.AddRange((page.MultipartUploads ?? []).Where(upload =>
                    bucket.Owns(upload.Key) && (!bucket.Keys.Contains(prefix) || upload.Key == prefix)));
                bool more = page.IsTruncated == true;
                keyMarker = more ? page.NextKeyMarker : null;
                uploadMarker = more ? page.NextUploadIdMarker : null;
            } while (keyMarker is not null);
        }
        catch (AmazonS3Exception error) when (error.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            unverifiable.Add($"{bucket.Label}: this key may not list open multipart uploads " +
                             $"({error.ErrorCode}), so none could be looked for under {prefix}");
            return null;
        }
        catch (Exception error)
        {
            errors.Add($"{bucket.Label}: list open uploads under {prefix}: {CheckReport.Describe(error)}");
            return null;
        }

        return open;
    }

    private static void Require(RunBucket bucket, IEnumerable<string> keys)
    {
        foreach (string key in keys)
        {
            if (!bucket.Owns(key))
            {
                throw new InvalidOperationException($"refusing to delete {key}, which this run didn't write");
            }
        }
    }
}
