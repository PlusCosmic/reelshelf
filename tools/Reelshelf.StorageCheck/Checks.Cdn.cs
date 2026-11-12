using System.Diagnostics;
using System.Net;
using Reelshelf.Games;
using Reelshelf.Moderation;
using Reelshelf.Plans;
using Reelshelf.Video.R2;

namespace Reelshelf.StorageCheck;

public sealed partial class StorageCheckRunner
{
    private bool _cdnServes;

    private string MediaUrl(string key) => $"{_settings.Store.MediaBaseUrl}/{key}";

    /// <summary>
    /// The media hostname as players, thumbnails and the Activity use it: plain GETs (and ranges, for seeking) of
    /// <c>clips/</c> and <c>game-art/</c>, nothing else readable, misses never cached, and HTTP sent to HTTPS.
    /// </summary>
    private async Task CdnAsync()
    {
        if (_settings.SkipCdn)
        {
            SkipRest("cdn", "--skip-cdn");
            return;
        }

        if (!_outputs.TryGetValue(R2ObjectKeys.Thumbnail, out byte[]? thumbnail) ||
            !_outputs.TryGetValue(R2ObjectKeys.Av1, out byte[]? av1))
        {
            SkipRest("cdn", "the worker's outputs aren't there to fetch");
            return;
        }

        _cdnServes = await Check("cdn.served", "a clip's thumbnail is served whole, immutable", async () =>
        {
            Fetched response = await GetAsync(_cdn, MediaUrl(R2ObjectKeys.Output(_run.ClipId, 1, R2ObjectKeys.Thumbnail)));
            Expect(response.Status == HttpStatusCode.OK, $"answered {response.Describe()}{Origin(response)}");
            Expect(response.Body.AsSpan().SequenceEqual(thumbnail), $"the bytes differ from the upload{Origin(response)}");
            Expect(response["Content-Type"] == "image/jpeg", $"Content-Type is '{response["Content-Type"]}'");
            Expect(response["Cache-Control"] == R2ObjectKeys.ImmutableCacheControl,
                $"Cache-Control is '{response["Cache-Control"]}'");
            if (response["X-Amz-Cf-Id"] is null)
            {
                Note("cdn.cloudfront", "who answered on the media hostname",
                    $"'{response["Server"] ?? "an unknown server"}', not CloudFront (no X-Amz-Cf-Id)");
            }

            return $"200, image/jpeg, {response["Cache-Control"]}";
        });
        if (!_cdnServes)
        {
            SkipRest("cdn", "the media hostname doesn't serve the run's files (see cdn.served)");
            return;
        }

        await Check("cdn.range", "a range of av1.mp4 is served as 206, as a player seeks", async () =>
        {
            string url = MediaUrl(R2ObjectKeys.Output(_run.ClipId, 1, R2ObjectKeys.Av1));
            Fetched response = await GetAsync(_cdn, url, "bytes=1000000-1065535");
            Expect(response.Status == HttpStatusCode.PartialContent, $"answered {response.Describe()}");
            Expect(response.Body.AsSpan().SequenceEqual(av1.AsSpan(1000000, 65536)), "the bytes differ from the upload");
            Expect(response["Content-Range"] == $"bytes 1000000-1065535/{av1.Length}", $"Content-Range is '{response["Content-Range"]}'");
            Expect(response["Content-Type"] == "video/mp4", $"Content-Type is '{response["Content-Type"]}'");
            Fetched end = await GetAsync(_cdn, url, $"bytes={av1.Length - 4096}-");
            Expect(end.Status == HttpStatusCode.PartialContent && end.Body.AsSpan().SequenceEqual(av1.AsSpan(av1.Length - 4096)),
                $"an open-ended range near the end answered {end.Describe()}");
            return $"206, {response["Content-Range"]}; an open-ended range too";
        });

        if (_gameArtKey is { } gameArt && _gameArt is { } cover)
        {
            await Check("cdn.game-art", "a game art copy is served", async () =>
            {
                Fetched response = await GetAsync(_cdn, MediaUrl(gameArt));
                Expect(response.Status == HttpStatusCode.OK, $"answered {response.Describe()}");
                Expect(response.Body.AsSpan().SequenceEqual(cover), "the bytes differ");
                Expect(response["Cache-Control"] == R2ObjectKeys.ImmutableCacheControl, $"Cache-Control is '{response["Cache-Control"]}'");
                return "200, immutable";
            });
        }

        List<(string Id, string What, string Key)> refused =
        [
            ("cdn.refuse-uploads", "an uploaded source", R2ObjectKeys.Upload(_run.ClipId))
        ];
        if (_quarantined)
        {
            refused.Add(("cdn.refuse-quarantine", "a quarantined object",
                EvidenceQuarantineKeys.For(_run.EvidenceId, ServerThumbnail)));
        }

        if (_exportKey is not null)
        {
            refused.Add(("cdn.refuse-exports", "an export zip", _exportKey));
        }

        foreach ((string id, string what, string key) in refused)
        {
            await Check(id, $"{what} that exists is not served (403)", async () =>
            {
                Fetched response = await GetAsync(_cdn, MediaUrl(key), "bytes=0-0");
                Expect(response.Status == HttpStatusCode.Forbidden, $"answered {response.Describe()} for {key}");
                return $"403 {response.ErrorCode} for {key}";
            });
        }

        await Check("cdn.miss-then-served", "a missing file is 403, and served as soon as it exists", async () =>
        {
            string key = R2ObjectKeys.Output(_run.ClipId, 2, R2ObjectKeys.Thumbnail);
            Fetched missing = await GetAsync(_cdn, MediaUrl(key));
            Expect(missing.Status == HttpStatusCode.Forbidden,
                $"a missing key answered {missing.Describe()}, not 403 (CloudFront may not list the bucket)");
            byte[] content = RandomBytes(2 * Kibibyte);
            await _media.PutAsync(key, content, "image/jpeg", R2ObjectKeys.ImmutableCacheControl, _cancellation);
            Stopwatch waited = Stopwatch.StartNew();
            Fetched served = await GetAsync(_cdn, MediaUrl(key));
            if (served.Status == HttpStatusCode.OK)
            {
                return $"403 {missing.ErrorCode} before; 200 at once after the upload (the miss wasn't cached)";
            }

            // How long the miss was cached, to say what went wrong; still a failure.
            while (waited.Elapsed < TimeSpan.FromSeconds(30) && served.Status != HttpStatusCode.OK)
            {
                await _services.Delay(TimeSpan.FromSeconds(2), _cancellation);
                served = await GetAsync(_cdn, MediaUrl(key));
            }

            throw new CheckFailedException(served.Status == HttpStatusCode.OK
                ? $"the miss was cached: served only after {waited.Elapsed.TotalSeconds:0} s"
                : $"still {served.Describe()} 30 s after the upload");
        });

        if (_settings.MediaConnectPort is not null)
        {
            Skip("cdn.https-redirect", "HTTP is sent to HTTPS", "--media-connect-host names a port, so port 80 isn't the CDN's");
        }
        else
        {
            await Check("cdn.https-redirect", "HTTP is sent to HTTPS", async () =>
            {
                string https = MediaUrl(R2ObjectKeys.Output(_run.ClipId, 1, R2ObjectKeys.Thumbnail));
                string http = "http://" + https["https://".Length..];
                Fetched response = await GetAsync(_cdn, http);
                Expect((int)response.Status is 301 or 302 or 307 or 308, $"answered {response.Describe()}");
                Expect(response["Location"] == https, $"Location is '{response["Location"]}'");
                return $"{(int)response.Status} to {response["Location"]}";
            });
        }
    }

    /// <summary>
    /// The purge as the evidence processor runs it: one invalidation of <c>/clips/{clipId}/*</c> and
    /// <c>/uploads/{clipId}*</c>. To see it take effect, a file is first cached at the edge and deleted from the bucket,
    /// so the edge serves it only from cache; once the invalidation reaches the edge it answers 403.
    /// </summary>
    private async Task PurgeAsync()
    {
        if (_services.Purger is not { } purgerFor || _settings.DistributionId is not { } distributionId)
        {
            SkipRest("purge", "Moderation__CloudFront__DistributionId isn't set");
            return;
        }

        string cachedKey = R2ObjectKeys.Output(_run.ClipId, 1, R2ObjectKeys.Thumbnails[1]);
        bool cached = false;
        if (_cdnServes && _outputs.ContainsKey(R2ObjectKeys.Thumbnails[1]))
        {
            cached = await Check("purge.cached", "a file cached at the edge is served after it is deleted from the bucket",
                async () =>
                {
                    string? cache = null;
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        Fetched response = await GetAsync(_cdn, MediaUrl(cachedKey));
                        Expect(response.Status == HttpStatusCode.OK, $"answered {response.Describe()}");
                        cache = response["X-Cache"];
                        if (cache?.StartsWith("Hit", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            break;
                        }
                    }

                    await _media.DeleteObjectsAsync([cachedKey]);
                    Fetched after = await GetAsync(_cdn, MediaUrl(cachedKey));
                    Expect(after.Status == HttpStatusCode.OK,
                        $"after the delete it answered {after.Describe()}, so it wasn't cached (X-Cache '{cache}')");
                    return $"X-Cache '{cache}'; still served from the cache after the delete";
                });
        }

        bool purged = await Check("purge.accepted", "CloudFrontCdnPurger.PurgeClipAsync is accepted", async () =>
        {
            ICdnPurger purger = purgerFor(distributionId);
            try
            {
                string result = await purger.PurgeClipAsync(_run.ClipId, 1, _cancellation);
                Expect(result == CdnPurgeResults.Purged, $"answered '{result}'");
            }
            finally
            {
                (purger as IDisposable)?.Dispose();
            }

            return $"one invalidation created: {string.Join(", ", CloudFrontCdnPurger.ClipPaths(_run.ClipId))}";
        });

        if (!purged || !cached)
        {
            Skip("purge.effective", "the invalidation takes effect", purged ? "nothing was cached to watch" : "no invalidation");
            return;
        }

        if (!_settings.PurgeWait)
        {
            Skip("purge.effective", "the invalidation takes effect", "--no-purge-wait");
            return;
        }

        await Check("purge.effective", $"the edge stops serving it (waiting up to {_settings.PurgeWaitLimit.TotalSeconds:0} s)",
            async () =>
            {
                Stopwatch waited = Stopwatch.StartNew();
                while (true)
                {
                    Fetched response = await GetAsync(_cdn, MediaUrl(cachedKey));
                    if (response.Status == HttpStatusCode.Forbidden)
                    {
                        return $"403 after {waited.Elapsed.TotalSeconds:0} s";
                    }

                    if (waited.Elapsed >= _settings.PurgeWaitLimit)
                    {
                        throw new CheckFailedException(
                            $"still {response.Describe()} after {waited.Elapsed.TotalSeconds:0} s; invalidations usually " +
                            "finish within minutes, so check its status in the CloudFront console");
                    }

                    await _services.Delay(TimeSpan.FromSeconds(10), _cancellation);
                }
            });
    }

    /// <summary>The deletes the API makes, before cleanup removes whatever is left anyway.</summary>
    private async Task DeletesAsync()
    {
        if (_quarantined)
        {
            await Check("delete.quarantine", "R2EvidenceQuarantine.DeleteAsync empties the quarantine", async () =>
            {
                await new R2EvidenceQuarantine(_media).DeleteAsync(_run.EvidenceId);
                List<(string Key, long Size)> left = await _media.ListAsync(EvidenceQuarantineKeys.Prefix(_run.EvidenceId));
                Expect(left.Count == 0, $"{left.Count} objects left");
                return "empty";
            });
        }

        if (_gameArtKey is { } gameArt)
        {
            await Check("delete.game-art", "R2GameArtStore.DeleteAsync removes the copy", async () =>
            {
                await new R2GameArtStore(_media).DeleteAsync(gameArt);
                Expect(await _media.GetObjectSizeAsync(gameArt, _cancellation) is null, "it is still there");
                return "gone";
            });
        }

        if (_exportKey is not null)
        {
            await Check("delete.export", "deleting an export's folder (DeletePrefixAsync)", async () =>
            {
                string prefix = ExportParts.Prefix(_run.ExportUserId, _run.ExportId);
                await new R2ExportStorage(_media).DeletePrefixAsync(prefix);
                Expect((await _media.ListAsync(prefix)).Count == 0, "parts are left");
                return "empty";
            });
        }

        await Check("delete.clip", "a clip's delete (abort, delete the upload, delete clips/{id}/) leaves nothing", async () =>
        {
            Guid clipId = _run.ClipId;
            await _media.AbortMultipartUploadsAsync(R2ObjectKeys.Upload(clipId));
            await _media.DeleteObjectsAsync([R2ObjectKeys.Upload(clipId)]);
            await _media.DeletePrefixAsync(R2ObjectKeys.ClipPrefix(clipId));
            Expect((await _media.ListAsync(R2ObjectKeys.ClipPrefix(clipId))).Count == 0, "outputs are left");
            Expect(await _media.GetObjectSizeAsync(R2ObjectKeys.Upload(clipId), _cancellation) is null, "the upload is left");
            return $"clips/{clipId}/ and uploads/{clipId} gone";
        });
    }

    /// <summary>Who answered, when it wasn't CloudFront: DNS for the media hostname may still point at the old CDN.</summary>
    private static string Origin(Fetched response) =>
        response["X-Amz-Cf-Id"] is not null
            ? ""
            : $" (answered by '{response["Server"] ?? "an unknown server"}', not CloudFront; until DNS moves, pass " +
              "--media-connect-host with the distribution's domain)";
}
