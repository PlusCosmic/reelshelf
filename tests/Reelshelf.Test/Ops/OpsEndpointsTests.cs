using System.Net;
using System.Text.Json;
using Dapper;
using Reelshelf.Devices;
using Reelshelf.Test.Devices;
using Xunit;

namespace Reelshelf.Test.Ops;

/// <summary>
/// <c>/api/admin/ops/</c> and the admin endpoints the monitor app reaches with a device token, run through the whole
/// app (<see cref="DeviceApi"/>) against fixture rows in a schema with every migration.
/// </summary>
public class OpsEndpointsTests
{
    private static readonly string[] OpsPaths =
        ["/api/admin/ops/storage", "/api/admin/ops/transcoding", "/api/admin/ops/ai"];

    private static readonly Dictionary<string, string?> FlagOn = new() { ["FeatureFlags:Overrides:ops-monitor"] = "true" };

    [PostgresFact]
    public async Task AnAdmin_ReadsTheOpsEndpoints_WithTheCookieOrADeviceToken()
    {
        await using DeviceApi api = await DeviceApi.StartAsync(FlagOn);
        Guid admin = await AddAdminAsync(api);
        string token = await api.SignInDeviceAsync(admin, "Monitor", "monitor");

        foreach (string path in OpsPaths)
        {
            using HttpResponseMessage cookie = await api.SendAsync(HttpMethod.Get, path, cookieUser: admin);
            Assert.Equal(HttpStatusCode.OK, cookie.StatusCode);
            using HttpResponseMessage device = await api.SendAsync(HttpMethod.Get, path, deviceToken: token);
            Assert.Equal(HttpStatusCode.OK, device.StatusCode);
        }
    }

    [PostgresFact]
    public async Task SomeoneWhoIsntAnAdmin_IsForbidden_AndNobodySignedIn_IsUnauthorized()
    {
        await using DeviceApi api = await DeviceApi.StartAsync(FlagOn);
        Guid editor = await api.AddUserAsync();
        // Even signed in as the monitor app, the permission keeps a non-Admin out.
        string token = await api.SignInDeviceAsync(editor, "Monitor", "monitor");

        foreach (string path in OpsPaths)
        {
            using HttpResponseMessage cookie = await api.SendAsync(HttpMethod.Get, path, cookieUser: editor);
            Assert.Equal(HttpStatusCode.Forbidden, cookie.StatusCode);
            using HttpResponseMessage device = await api.SendAsync(HttpMethod.Get, path, deviceToken: token);
            Assert.Equal(HttpStatusCode.Forbidden, device.StatusCode);
            using HttpResponseMessage anonymous = await api.SendAsync(HttpMethod.Get, path);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        using HttpResponseMessage accounts =
            await api.SendAsync(HttpMethod.Get, "/api/admin/accounts?query=player", deviceToken: token);
        Assert.Equal(HttpStatusCode.Forbidden, accounts.StatusCode);
        using HttpResponseMessage reports = await api.SendAsync(HttpMethod.Get, "/api/admin/reports", deviceToken: token);
        Assert.Equal(HttpStatusCode.Forbidden, reports.StatusCode);
    }

    [PostgresFact]
    public async Task WhileTheFlagIsOff_TheOpsEndpoints_AreNotFound()
    {
        await using DeviceApi api = await DeviceApi.StartAsync(
            new Dictionary<string, string?> { ["FeatureFlags:Overrides:ops-monitor"] = "false" });
        Guid admin = await AddAdminAsync(api);
        string token = await api.SignInDeviceAsync(admin, "Monitor", "monitor");

        foreach (string path in OpsPaths)
        {
            using HttpResponseMessage cookie = await api.SendAsync(HttpMethod.Get, path, cookieUser: admin);
            Assert.Equal(HttpStatusCode.NotFound, cookie.StatusCode);
            using HttpResponseMessage device = await api.SendAsync(HttpMethod.Get, path, deviceToken: token);
            Assert.Equal(HttpStatusCode.NotFound, device.StatusCode);
        }

        // The flag gates the ops endpoints only.
        using HttpResponseMessage accounts =
            await api.SendAsync(HttpMethod.Get, "/api/admin/accounts?query=player", deviceToken: token);
        Assert.Equal(HttpStatusCode.OK, accounts.StatusCode);
    }

    [PostgresFact]
    public async Task AnAdminsDeviceToken_ReachesMe_TheFlags_AccountsAndReports_UntilItIsRevoked()
    {
        await using DeviceApi api = await DeviceApi.StartAsync(FlagOn);
        Guid admin = await AddAdminAsync(api);
        string token = await api.SignInDeviceAsync(admin, "Monitor", "monitor");

        using (HttpResponseMessage me = await api.SendAsync(HttpMethod.Get, "/api/me", deviceToken: token))
        {
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
            using JsonDocument body = await ReadAsync(me);
            Assert.Equal(admin, body.RootElement.GetProperty("id").GetGuid());
            Assert.True(body.RootElement.GetProperty("is_admin").GetBoolean());
        }

        using (HttpResponseMessage features =
               await api.SendAsync(HttpMethod.Get, "/api/me/features", deviceToken: token))
        {
            Assert.Equal(HttpStatusCode.OK, features.StatusCode);
            using JsonDocument body = await ReadAsync(features);
            Assert.Contains("ops-monitor",
                body.RootElement.GetProperty("enabled").EnumerateArray().Select(flag => flag.GetString()));
        }

        using (HttpResponseMessage accounts =
               await api.SendAsync(HttpMethod.Get, "/api/admin/accounts?query=admin", deviceToken: token))
        {
            Assert.Equal(HttpStatusCode.OK, accounts.StatusCode);
        }

        using (HttpResponseMessage account =
               await api.SendAsync(HttpMethod.Get, $"/api/admin/accounts/{admin}", deviceToken: token))
        {
            Assert.Equal(HttpStatusCode.OK, account.StatusCode);
        }

        using (HttpResponseMessage reports = await api.SendAsync(HttpMethod.Get, "/api/admin/reports", deviceToken: token))
        {
            Assert.Equal(HttpStatusCode.OK, reports.StatusCode);
        }

        // Removing the device in Settings cuts it off everywhere at once.
        Guid deviceId = await api.Connection.ExecuteScalarAsync<Guid>(
            "SELECT id FROM device_token WHERE app_user_id = @admin", new { admin });
        using (HttpResponseMessage revoke =
               await api.SendAsync(HttpMethod.Delete, $"/api/me/devices/{deviceId}", cookieUser: admin))
        {
            Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        }

        foreach (string path in OpsPaths.Concat(["/api/me", "/api/me/features", "/api/admin/reports"]))
        {
            using HttpResponseMessage refused = await api.SendAsync(HttpMethod.Get, path, deviceToken: token);
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        }
    }

    private static readonly string[] AdminPaths =
        [.. OpsPaths, "/api/admin/accounts?query=admin", "/api/admin/reports"];

    [PostgresFact]
    public async Task AnAdminsUploaderToken_IsForbidden_OnEveryAdminGroup()
    {
        await using DeviceApi api = await DeviceApi.StartAsync(FlagOn);
        Guid admin = await AddAdminAsync(api);
        // The shipped uploader names no client, so its token is the uploader's.
        string uploader = await api.SignInDeviceAsync(admin);
        string named = await api.SignInDeviceAsync(admin, "Laptop", "uploader");
        Assert.Equal(["uploader", "uploader"],
            await api.Connection.QueryAsync<string>("SELECT client FROM device_token WHERE app_user_id = @admin",
                new { admin }));

        foreach (string path in AdminPaths)
        {
            foreach (string token in new[] { uploader, named })
            {
                // The token authenticates, so the policy refuses it with 403, not 401.
                using HttpResponseMessage refused = await api.SendAsync(HttpMethod.Get, path, deviceToken: token);
                Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            }
        }

        // It still reaches what the uploader and every device may use.
        foreach (string path in new[] { "/api/me", "/api/me/features", "/api/me/plan", "/api/games/categories" })
        {
            using HttpResponseMessage allowed = await api.SendAsync(HttpMethod.Get, path, deviceToken: uploader);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }
    }

    [PostgresFact]
    public async Task ATokenIssuedBeforeClientsExisted_IsTheUploaders()
    {
        await using DeviceApi api = await DeviceApi.StartAsync(FlagOn);
        Guid admin = await AddAdminAsync(api);
        string token = DeviceCodes.NewToken();
        // Written as the image before V68 writes it, without the column.
        await api.Connection.ExecuteAsync("""
            INSERT INTO device_token (id, app_user_id, name, token_hash) VALUES (@id, @admin, 'Old PC', @hash)
            """, new { id = Guid.NewGuid(), admin, hash = DeviceCodes.Hash(token) });

        Assert.Equal("uploader", await api.Connection.ExecuteScalarAsync<string>(
            "SELECT client FROM device_token WHERE app_user_id = @admin", new { admin }));
        foreach (string path in AdminPaths)
        {
            using HttpResponseMessage refused = await api.SendAsync(HttpMethod.Get, path, deviceToken: token);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        using HttpResponseMessage plan = await api.SendAsync(HttpMethod.Get, "/api/me/plan", deviceToken: token);
        Assert.Equal(HttpStatusCode.OK, plan.StatusCode);
    }

    [PostgresFact]
    public async Task TheMonitorsSignIn_SaysWhichAppIsAsking_AndAnUnknownAppIsRefused()
    {
        await using DeviceApi api = await DeviceApi.StartAsync(FlagOn);
        Guid admin = await AddAdminAsync(api);

        using HttpResponseMessage unknown = await api.SendAsync(HttpMethod.Post, "/api/devices/authorize",
            json: new { device_name = "Mac", client = "toaster" });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        using HttpResponseMessage authorize = await api.SendAsync(HttpMethod.Post, "/api/devices/authorize",
            json: new { device_name = "Mac", client = "monitor" });
        Assert.Equal(HttpStatusCode.OK, authorize.StatusCode);
        using JsonDocument started = await ReadAsync(authorize);
        string userCode = started.RootElement.GetProperty("user_code").GetString()!;

        using HttpResponseMessage shown = await api.SendAsync(HttpMethod.Get, $"/api/devices/authorize/{userCode}",
            cookieUser: admin);
        using JsonDocument request = await ReadAsync(shown);
        Assert.Equal("monitor", request.RootElement.GetProperty("client").GetString());

        await api.SignInDeviceAsync(admin, "Studio Mac", "monitor");
        using HttpResponseMessage listed = await api.SendAsync(HttpMethod.Get, "/api/me/devices", cookieUser: admin);
        using JsonDocument devices = await ReadAsync(listed);
        Assert.Equal("monitor", devices.RootElement[0].GetProperty("client").GetString());
    }

    [PostgresFact]
    public async Task Storage_AddsUpClipsByTierAndAccount_AndTheExportsHeld()
    {
        await using DeviceApi api = await DeviceApi.StartAsync(FlagOn);
        Guid admin = await AddAdminAsync(api);
        Guid free = await api.AddUserAsync("free");
        Guid granted = await api.AddUserAsync("granted");
        Guid paying = await api.AddUserAsync("paying");
        await api.Connection.ExecuteAsync("""
            UPDATE app_user SET granted_tier = 'unlimited' WHERE id = @granted;
            INSERT INTO billing_subscription (id, user_id, status, tier) VALUES
                ('sub_pro', @paying, 'active', 'pro'),
                ('sub_old', @free, 'canceled', 'pro');
            """, new { granted, paying, free });

        Guid book = await api.AddBookAsync(free, "Game");
        // Free: 100 + 200 (binned) bytes; the larger of file_size and storage_size counts.
        await AddSizedClipAsync(api, free, book, fileSize: 100, storageSize: 40);
        await AddSizedClipAsync(api, free, book, fileSize: null, storageSize: 200, binned: true);
        // Unlimited (granted): 1,000 bytes.
        await AddSizedClipAsync(api, granted, book, fileSize: 1_000, storageSize: null);
        // Pro (paying): 50 + 0 bytes.
        await AddSizedClipAsync(api, paying, book, fileSize: null, storageSize: 50);
        await AddSizedClipAsync(api, paying, book, fileSize: null, storageSize: null);

        await api.Connection.ExecuteAsync("""
            INSERT INTO clip_export (user_id, reason, status, clip_ids, parts, completed_at, expires_at) VALUES
                (@free, 'requested', 'ready', '{}', '[{"index":1,"size_bytes":700,"clip_count":2},{"index":2,"size_bytes":300,"clip_count":1}]',
                 now(), now() + interval '28 days'),
                (@granted, 'requested', 'expired', '{}', '[{"index":1,"size_bytes":9999,"clip_count":1}]',
                 now(), now()),
                (@paying, 'grace_ended', 'pending', '{}', '[]', NULL, NULL);
            """, new { free, granted, paying });

        using JsonDocument body = await GetAsync(api, "/api/admin/ops/storage", admin);
        JsonElement root = body.RootElement;

        JsonElement store = root.GetProperty("store");
        Assert.True(store.GetProperty("configured").GetBoolean());
        Assert.Equal("s3-compatible", store.GetProperty("kind").GetString());
        Assert.False(string.IsNullOrEmpty(store.GetProperty("bucket").GetString()));
        Assert.DoesNotContain("secret", store.GetRawText(), StringComparison.OrdinalIgnoreCase);

        JsonElement total = root.GetProperty("total");
        Assert.Equal(5, total.GetProperty("clips").GetInt64());
        Assert.Equal(1_350, total.GetProperty("bytes").GetInt64());
        Assert.Equal(1, total.GetProperty("binned_clips").GetInt64());
        Assert.Equal(200, total.GetProperty("binned_bytes").GetInt64());

        Dictionary<string, JsonElement> tiers = root.GetProperty("by_tier").EnumerateArray()
            .ToDictionary(tier => tier.GetProperty("tier").GetString()!);
        Assert.Equal(["free", "plus", "pro", "unlimited"], tiers.Keys);
        AssertTier(tiers["free"], accounts: 1, clips: 2, bytes: 300);
        AssertTier(tiers["plus"], accounts: 0, clips: 0, bytes: 0);
        AssertTier(tiers["pro"], accounts: 1, clips: 2, bytes: 50);
        AssertTier(tiers["unlimited"], accounts: 1, clips: 1, bytes: 1_000);

        List<JsonElement> top = root.GetProperty("top_accounts").EnumerateArray().ToList();
        Assert.Equal([granted, free, paying], top.Select(account => account.GetProperty("user_id").GetGuid()));
        Assert.Equal("unlimited", top[0].GetProperty("tier").GetString());
        Assert.Equal(1_000, top[0].GetProperty("bytes").GetInt64());
        Assert.Equal(2, top[2].GetProperty("clips").GetInt64());
        Assert.DoesNotContain(admin, top.Select(account => account.GetProperty("user_id").GetGuid()));

        JsonElement exports = root.GetProperty("exports");
        Assert.Equal(1, exports.GetProperty("held").GetInt64());
        Assert.Equal(1_000, exports.GetProperty("held_bytes").GetInt64());
        Assert.Equal(1, exports.GetProperty("pending").GetInt64());
        Assert.Equal(0, exports.GetProperty("running").GetInt64());
    }

    [PostgresFact]
    public async Task Transcoding_CountsTheQueueByType_ListsFailures_AndTheAacBackfill()
    {
        await using DeviceApi api = await DeviceApi.StartAsync(FlagOn);
        Guid admin = await AddAdminAsync(api);
        Guid owner = await api.AddUserAsync();
        Guid book = await api.AddBookAsync(owner, "Game");
        Guid clip = await api.AddClipAsync(owner, book, "ready");
        Guid opus = await api.AddClipAsync(owner, book, "ready");
        await api.AddClipAsync(owner, book, "processing");
        await api.Connection.ExecuteAsync("""
            UPDATE clip SET video_codecs = 'av01.0.08M.08,opus' WHERE id = @opus;
            UPDATE clip SET video_codecs = 'av01.0.08M.08,mp4a.40.2' WHERE id = @clip;
            INSERT INTO transcode_run (clip_id, type, status, attempts, available_at, created_at, heartbeat_at,
                                       started_at, finished_at, error) VALUES
                -- Claimable now, queued 10 minutes ago.
                (@clip, 'trim', 'pending', 0, now(), now() - interval '10 minutes', NULL, NULL, NULL, NULL),
                -- Waiting out its retry delay.
                (@clip, 'trim', 'pending', 1, now() + interval '1 minute', now() - interval '2 minutes', NULL, NULL, NULL, NULL),
                -- Running, and one gone quiet.
                (@clip, 'thumbnails', 'running', 1, now(), now(), now(), now(), NULL, NULL),
                (@clip, 'thumbnails', 'running', 1, now(), now(), now() - interval '1 hour', now() - interval '1 hour', NULL, NULL),
                -- Finished in the last hour, and earlier today.
                (@clip, 'audio', 'succeeded', 1, now(), now(), NULL, now(), now() - interval '5 minutes', NULL),
                (@clip, 'audio', 'failed', 3, now(), now(), NULL, now(), now() - interval '10 minutes', 'Gave up after 3 attempts'),
                (@clip, 'audio', 'failed', 1, now(), now(), NULL, now(), now() - interval '5 hours', 'unreadable'),
                (@clip, 'audio', 'succeeded', 1, now(), now(), NULL, now(), now() - interval '2 days', NULL);
            """, new { clip, opus });

        using JsonDocument body = await GetAsync(api, "/api/admin/ops/transcoding", admin);
        JsonElement root = body.RootElement;
        Dictionary<string, JsonElement> types = root.GetProperty("types").EnumerateArray()
            .ToDictionary(type => type.GetProperty("name").GetString()!);
        Assert.Contains("transcode", types.Keys);

        JsonElement trim = types["trim"];
        Assert.Equal(1, trim.GetProperty("pending").GetInt64());
        Assert.Equal(1, trim.GetProperty("retrying").GetInt64());
        Assert.InRange(trim.GetProperty("oldest_pending_age_seconds").GetDouble(), 9 * 60, 11 * 60);

        JsonElement thumbnails = types["thumbnails"];
        Assert.Equal(2, thumbnails.GetProperty("running").GetInt64());
        Assert.Equal(1, thumbnails.GetProperty("stale_running").GetInt64());

        JsonElement audio = types["audio"];
        Assert.Equal(1, audio.GetProperty("last_hour").GetProperty("succeeded").GetInt64());
        Assert.Equal(1, audio.GetProperty("last_hour").GetProperty("failed").GetInt64());
        Assert.Equal(1, audio.GetProperty("last_hour").GetProperty("exhausted").GetInt64());
        Assert.Equal(1, audio.GetProperty("last_day").GetProperty("succeeded").GetInt64());
        Assert.Equal(2, audio.GetProperty("last_day").GetProperty("failed").GetInt64());
        Assert.Equal(1, audio.GetProperty("last_day").GetProperty("exhausted").GetInt64());

        List<JsonElement> failures = root.GetProperty("recent_failures").EnumerateArray().ToList();
        Assert.Equal(["Gave up after 3 attempts", "unreadable"], failures.Select(f => f.GetProperty("error").GetString()));
        Assert.Equal(clip, failures[0].GetProperty("clip_id").GetGuid());
        Assert.Equal(3, failures[0].GetProperty("attempts").GetInt32());

        JsonElement overflow = root.GetProperty("overflow");
        Assert.False(overflow.GetProperty("enabled").GetBoolean());
        Assert.Empty(overflow.GetProperty("pools").EnumerateArray());

        JsonElement aac = root.GetProperty("aac_backfill");
        Assert.False(aac.GetProperty("enabled").GetBoolean());
        Assert.Equal(1, aac.GetProperty("remaining").GetInt64());
    }

    [PostgresFact]
    public async Task Ai_CountsEachQueue_ListsFailures_AndAddsUpSpendByModel()
    {
        await using DeviceApi api = await DeviceApi.StartAsync(FlagOn);
        Guid admin = await AddAdminAsync(api);
        Guid owner = await api.AddUserAsync();
        Guid book = await api.AddBookAsync(owner, "Game");
        Guid clip = await api.AddClipAsync(owner, book, "ready");
        await api.Connection.ExecuteAsync("""
            INSERT INTO legend_model_price (provider, model, input_per_mtok, output_per_mtok)
            VALUES ('openai', 'priced', 2, 10);

            INSERT INTO legend_detection_run (clip_id, trigger, provider, model, status, attempts, input_tokens,
                                              output_tokens, cost_usd, error, created_at, started_at, completed_at,
                                              archived_at) VALUES
                -- Billed by the provider: 0.25 today, 1.00 last week.
                (@clip, 'manual', 'openrouter', 'billed', 'succeeded', 1, 100, 10, 0.25, NULL, now(), now(), now() - interval '1 hour', NULL),
                (@clip, 'manual', 'openrouter', 'billed', 'succeeded', 1, 100, 10, 1.00, NULL, now(), now(), now() - interval '7 days', NULL),
                -- Priced: 1,000,000 input at 2 and 100,000 output at 10 is 3.00, failed or not.
                (@clip, 'manual', 'openai', 'priced', 'failed', 3, 1000000, 100000, NULL, 'refused', now(), now(), now() - interval '2 hours', NULL),
                -- No price: counted, but unpriced.
                (@clip, 'eval', 'openai', 'unpriced', 'succeeded', 1, 50, 5, NULL, NULL, now(), now(), now() - interval '3 hours', NULL),
                -- Too old to count.
                (@clip, 'manual', 'openrouter', 'billed', 'succeeded', 1, 100, 10, 5.00, NULL, now(), now(), now() - interval '40 days', NULL),
                -- Waiting, and an archived failure left out of the queue.
                (@clip, 'auto', 'openai', 'priced', 'pending', 0, NULL, NULL, NULL, NULL, now() - interval '3 minutes', NULL, NULL, NULL),
                (@clip, 'manual', 'openai', 'priced', 'failed', 3, NULL, NULL, NULL, 'archived', now(), now(), now(), now());

            INSERT INTO clip_transcription_run (id, clip_id, trigger, model, status, attempts, input_tokens, output_tokens,
                                                error, started_at, completed_at) VALUES
                ('00000000-0000-0000-0000-000000000001', @clip, 'auto', 'google/gemini-3.5-transcribe', 'succeeded', 1, 10, 20, NULL, now(), now()),
                ('00000000-0000-0000-0000-000000000002', @clip, 'manual', 'deepgram/nova-3', 'failed', 3, NULL, NULL, 'timeout', now(), now());

            INSERT INTO clip_summary_run (clip_id, source_transcription_run_id, trigger, provider, model, status, attempts,
                                          embedding_model, input_tokens, output_tokens, started_at, completed_at) VALUES
                (@clip, '00000000-0000-0000-0000-000000000001', 'auto', 'openai', 'gpt-summary', 'succeeded', 1,
                 'text-embedding-3-small', 300, 40, now(), now());
            """, new { clip });

        using JsonDocument body = await GetAsync(api, "/api/admin/ops/ai", admin);
        JsonElement root = body.RootElement;
        Dictionary<string, JsonElement> queues = root.GetProperty("queues").EnumerateArray()
            .ToDictionary(queue => queue.GetProperty("queue").GetString()!);
        Assert.Equal(["legend_detection", "clip_transcription", "clip_summary"], queues.Keys);

        Dictionary<string, JsonElement> legendTriggers = queues["legend_detection"].GetProperty("triggers")
            .EnumerateArray().ToDictionary(trigger => trigger.GetProperty("name").GetString()!);
        Assert.Equal(1, legendTriggers["auto"].GetProperty("pending").GetInt64());
        Assert.Equal(1, legendTriggers["manual"].GetProperty("last_day").GetProperty("succeeded").GetInt64());
        Assert.Equal(1, legendTriggers["manual"].GetProperty("last_day").GetProperty("failed").GetInt64());
        Assert.Equal(0, legendTriggers["manual"].GetProperty("last_hour").GetProperty("failed").GetInt64());
        List<JsonElement> legendFailures = queues["legend_detection"].GetProperty("recent_failures").EnumerateArray().ToList();
        Assert.Equal(["refused"], legendFailures.Select(f => f.GetProperty("error").GetString()));
        Assert.Equal("openai", legendFailures[0].GetProperty("provider").GetString());

        List<JsonElement> transcriptionFailures =
            queues["clip_transcription"].GetProperty("recent_failures").EnumerateArray().ToList();
        Assert.Equal(["timeout"], transcriptionFailures.Select(f => f.GetProperty("error").GetString()));
        Assert.Equal(JsonValueKind.Null, transcriptionFailures[0].GetProperty("provider").ValueKind);
        Assert.Empty(queues["clip_summary"].GetProperty("recent_failures").EnumerateArray());

        Dictionary<string, JsonElement> spend = root.GetProperty("spend").EnumerateArray()
            .ToDictionary(row => $"{row.GetProperty("queue").GetString()}/{row.GetProperty("model").GetString()}");

        JsonElement billed = spend["legend_detection/billed"];
        Assert.Equal(1, billed.GetProperty("last_day").GetProperty("runs").GetInt64());
        Assert.Equal(0.25m, billed.GetProperty("last_day").GetProperty("cost_usd").GetDecimal());
        Assert.Equal(2, billed.GetProperty("last_month").GetProperty("runs").GetInt64());
        Assert.Equal(1.25m, billed.GetProperty("last_month").GetProperty("cost_usd").GetDecimal());
        Assert.Equal(200, billed.GetProperty("last_month").GetProperty("input_tokens").GetInt64());

        JsonElement priced = spend["legend_detection/priced"];
        Assert.Equal(3.00m, priced.GetProperty("last_day").GetProperty("cost_usd").GetDecimal());
        Assert.Equal(0, priced.GetProperty("last_day").GetProperty("unpriced_runs").GetInt64());

        JsonElement unpriced = spend["legend_detection/unpriced"];
        Assert.Equal(JsonValueKind.Null, unpriced.GetProperty("last_day").GetProperty("cost_usd").ValueKind);
        Assert.Equal(1, unpriced.GetProperty("last_day").GetProperty("unpriced_runs").GetInt64());

        JsonElement summary = spend["clip_summary/gpt-summary"];
        Assert.Equal(300, summary.GetProperty("last_day").GetProperty("input_tokens").GetInt64());
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("last_day").GetProperty("cost_usd").ValueKind);
        Assert.True(spend.ContainsKey("clip_transcription/deepgram/nova-3"));
    }

    [PostgresFact]
    public async Task AResponse_IsKeptForFifteenSeconds()
    {
        await using DeviceApi api = await DeviceApi.StartAsync(FlagOn);
        Guid admin = await AddAdminAsync(api);

        using JsonDocument first = await GetAsync(api, "/api/admin/ops/storage", admin);
        Guid owner = await api.AddUserAsync();
        await AddSizedClipAsync(api, owner, await api.AddBookAsync(owner, "Game"), fileSize: 10, storageSize: null);
        using JsonDocument second = await GetAsync(api, "/api/admin/ops/storage", admin);

        Assert.Equal(first.RootElement.GetProperty("generated_at").GetString(),
            second.RootElement.GetProperty("generated_at").GetString());
        Assert.Equal(0, second.RootElement.GetProperty("total").GetProperty("clips").GetInt64());
    }

    private static async Task<Guid> AddAdminAsync(DeviceApi api)
    {
        Guid admin = await api.AddUserAsync("admin");
        await api.Connection.ExecuteAsync("UPDATE app_user SET role = 'Admin' WHERE id = @admin", new { admin });
        return admin;
    }

    private static async Task AddSizedClipAsync(DeviceApi api, Guid owner, Guid book, long? fileSize, long? storageSize,
        bool binned = false)
    {
        Guid clip = await api.AddClipAsync(owner, book, "ready");
        await api.Connection.ExecuteAsync("""
            UPDATE clip SET file_size = @fileSize, storage_size = @storageSize,
                            binned_at = CASE WHEN @binned THEN now() END
            WHERE id = @clip
            """, new { clip, fileSize, storageSize, binned });
    }

    private static void AssertTier(JsonElement tier, long accounts, long clips, long bytes)
    {
        Assert.Equal(accounts, tier.GetProperty("accounts").GetInt64());
        Assert.Equal(clips, tier.GetProperty("clips").GetInt64());
        Assert.Equal(bytes, tier.GetProperty("bytes").GetInt64());
    }

    private static async Task<JsonDocument> GetAsync(DeviceApi api, string path, Guid admin)
    {
        using HttpResponseMessage response = await api.SendAsync(HttpMethod.Get, path, cookieUser: admin);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync(response);
    }

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
}
