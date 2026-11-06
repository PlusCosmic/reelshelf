using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Reelshelf.Devices;
using Xunit;
using static Reelshelf.Test.Devices.DeviceApi;

namespace Reelshelf.Test.Devices;

/// <summary>
/// Device sign-in (ADR-0010) through the API as it is mapped: the RFC 8628 flow, deciding on <c>/device</c>, what a
/// device token may and may not reach, revoking, suspension, the version gate and the sweep.
/// </summary>
public class DeviceSignInTests
{
    [PostgresFact]
    public async Task TheWholeFlow_EndsWithATokenThatListsTheBooks()
    {
        await using DeviceApi api = await StartAsync();
        Guid user = await api.AddUserAsync();
        Guid book = await api.AddBookAsync(user, "Apex Legends");

        using HttpResponseMessage authorize = await api.SendAsync(HttpMethod.Post, "/api/devices/authorize",
            json: new { device_name = "  Gaming PC  " });
        Assert.Equal(HttpStatusCode.OK, authorize.StatusCode);
        DeviceAuthorizationJson started = (await authorize.Content.ReadFromJsonAsync<DeviceAuthorizationJson>())!;
        Assert.Matches("^[BCDFGHJKLMNPQRSTVWXZ]{4}-[BCDFGHJKLMNPQRSTVWXZ]{4}$", started.user_code);
        Assert.Equal("https://reelshelf.test/device", started.verification_uri);
        Assert.Equal($"https://reelshelf.test/device?code={started.user_code}", started.verification_uri_complete);
        Assert.Equal(5, started.interval);
        Assert.Equal(600, started.expires_in);

        // Only hashes are stored, and the user code without its dash.
        (byte[] storedHash, string storedCode, string storedName) = await api.Connection
            .QuerySingleAsync<(byte[], string, string)>(
                "SELECT device_code_hash, user_code, device_name FROM device_authorization");
        Assert.Equal(DeviceCodes.Hash(started.device_code), storedHash);
        Assert.Equal(started.user_code.Replace("-", ""), storedCode);
        Assert.Equal("Gaming PC", storedName);

        Assert.Equal("authorization_pending", await PollErrorAsync(api, started.device_code));
        Assert.Equal("slow_down", await PollErrorAsync(api, started.device_code));

        // /device shows the request, by its code typed any way.
        using HttpResponseMessage shown = await api.SendAsync(HttpMethod.Get,
            $"/api/devices/authorize/{started.user_code.ToLowerInvariant().Replace("-", "")}", cookieUser: user);
        Assert.Equal(HttpStatusCode.OK, shown.StatusCode);
        using JsonDocument request = JsonDocument.Parse(await shown.Content.ReadAsStringAsync());
        Assert.Equal("Gaming PC", request.RootElement.GetProperty("device_name").GetString());
        Assert.True(request.RootElement.TryGetProperty("expires_at", out _));

        using HttpResponseMessage approve = await api.SendAsync(HttpMethod.Post,
            $"/api/devices/authorize/{started.user_code}/approve", cookieUser: user);
        Assert.Equal(HttpStatusCode.NoContent, approve.StatusCode);

        // Still too soon after the last poll; then, once the interval has passed, the token.
        Assert.Equal("slow_down", await PollErrorAsync(api, started.device_code));
        await PassPollIntervalAsync(api);
        using HttpResponseMessage token = await api.SendAsync(HttpMethod.Post, "/api/devices/token",
            json: new { device_code = started.device_code });
        Assert.Equal(HttpStatusCode.OK, token.StatusCode);
        Assert.Contains("no-store", token.Headers.CacheControl?.ToString());
        DeviceTokenJson issued = (await token.Content.ReadFromJsonAsync<DeviceTokenJson>())!;
        Assert.StartsWith("rsd_", issued.access_token);
        Assert.Equal("Bearer", issued.token_type);

        // The request is gone and the token is stored as a hash, under the device's name.
        Assert.Equal(0, await api.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM device_authorization"));
        (Guid owner, string name, byte[] tokenHash) = await api.Connection
            .QuerySingleAsync<(Guid, string, byte[])>("SELECT app_user_id, name, token_hash FROM device_token");
        Assert.Equal(user, owner);
        Assert.Equal("Gaming PC", name);
        Assert.Equal(DeviceCodes.Hash(issued.access_token), tokenHash);

        using HttpResponseMessage books = await api.SendAsync(HttpMethod.Get, "/api/games/categories",
            deviceToken: issued.access_token);
        Assert.Equal(HttpStatusCode.OK, books.StatusCode);
        Assert.Contains(book.ToString(), await books.Content.ReadAsStringAsync());

        using HttpResponseMessage plan = await api.SendAsync(HttpMethod.Get, "/api/me/plan",
            deviceToken: issued.access_token);
        Assert.Equal(HttpStatusCode.OK, plan.StatusCode);
    }

    [PostgresFact]
    public async Task ADeniedRequest_AnswersAccessDenied_AndCantBeApprovedAfter()
    {
        await using DeviceApi api = await StartAsync();
        Guid user = await api.AddUserAsync();
        DeviceAuthorizationJson started = await AuthorizeAsync(api);

        using HttpResponseMessage deny = await api.SendAsync(HttpMethod.Post,
            $"/api/devices/authorize/{started.user_code}/deny", cookieUser: user);
        Assert.Equal(HttpStatusCode.NoContent, deny.StatusCode);

        using HttpResponseMessage approve = await api.SendAsync(HttpMethod.Post,
            $"/api/devices/authorize/{started.user_code}/approve", cookieUser: user);
        Assert.Equal(HttpStatusCode.Conflict, approve.StatusCode);
        using HttpResponseMessage denyAgain = await api.SendAsync(HttpMethod.Post,
            $"/api/devices/authorize/{started.user_code}/deny", cookieUser: user);
        Assert.Equal(HttpStatusCode.Conflict, denyAgain.StatusCode);
        using HttpResponseMessage shown = await api.SendAsync(HttpMethod.Get,
            $"/api/devices/authorize/{started.user_code}", cookieUser: user);
        Assert.Equal(HttpStatusCode.NotFound, shown.StatusCode);

        Assert.Equal("access_denied", await PollErrorAsync(api, started.device_code));
        Assert.Equal(0, await api.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM device_token"));
    }

    [PostgresFact]
    public async Task AnExpiredRequest_IsUnknownOnTheWebsite_AndAnswersExpiredToken()
    {
        await using DeviceApi api = await StartAsync();
        Guid user = await api.AddUserAsync();
        DeviceAuthorizationJson started = await AuthorizeAsync(api);
        await api.Connection.ExecuteAsync("UPDATE device_authorization SET expires_at = now() - interval '1 second'");

        using HttpResponseMessage shown = await api.SendAsync(HttpMethod.Get,
            $"/api/devices/authorize/{started.user_code}", cookieUser: user);
        Assert.Equal(HttpStatusCode.NotFound, shown.StatusCode);
        using HttpResponseMessage approve = await api.SendAsync(HttpMethod.Post,
            $"/api/devices/authorize/{started.user_code}/approve", cookieUser: user);
        Assert.Equal(HttpStatusCode.NotFound, approve.StatusCode);
        Assert.Equal("expired_token", await PollErrorAsync(api, started.device_code));
    }

    [PostgresFact]
    public async Task ACodeUsedTwice_GetsNoSecondToken()
    {
        await using DeviceApi api = await StartAsync();
        Guid user = await api.AddUserAsync();
        DeviceAuthorizationJson started = await AuthorizeAsync(api);
        using (HttpResponseMessage approve = await api.SendAsync(HttpMethod.Post,
                   $"/api/devices/authorize/{started.user_code}/approve", cookieUser: user))
        {
            Assert.Equal(HttpStatusCode.NoContent, approve.StatusCode);
        }

        using (HttpResponseMessage first = await api.SendAsync(HttpMethod.Post, "/api/devices/token",
                   json: new { device_code = started.device_code }))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        await PassPollIntervalAsync(api);
        Assert.Equal("expired_token", await PollErrorAsync(api, started.device_code));
        Assert.Equal("expired_token", await PollErrorAsync(api, "not-a-code"));
        using HttpResponseMessage approveAgain = await api.SendAsync(HttpMethod.Post,
            $"/api/devices/authorize/{started.user_code}/approve", cookieUser: user);
        Assert.Equal(HttpStatusCode.NotFound, approveAgain.StatusCode);
        Assert.Equal(1, await api.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM device_token"));
    }

    [PostgresFact]
    public async Task ADeviceName_MustBeGiven_AndFitIn64Characters()
    {
        await using DeviceApi api = await StartAsync();

        using HttpResponseMessage empty = await api.SendAsync(HttpMethod.Post, "/api/devices/authorize",
            json: new { device_name = "   " });
        using HttpResponseMessage tooLong = await api.SendAsync(HttpMethod.Post, "/api/devices/authorize",
            json: new { device_name = new string('x', 65) });

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [PostgresFact]
    public async Task WhileTheFlagIsOff_ApprovingAndTheDevicesList_AreNotFound()
    {
        await using DeviceApi api = await StartAsync(new Dictionary<string, string?>
        {
            ["FeatureFlags:Overrides:desktop-uploader"] = "false"
        });
        Guid user = await api.AddUserAsync();
        DeviceAuthorizationJson started = await AuthorizeAsync(api);

        using HttpResponseMessage approve = await api.SendAsync(HttpMethod.Post,
            $"/api/devices/authorize/{started.user_code}/approve", cookieUser: user);
        using HttpResponseMessage devices = await api.SendAsync(HttpMethod.Get, "/api/me/devices", cookieUser: user);

        Assert.Equal(HttpStatusCode.NotFound, approve.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, devices.StatusCode);
        Assert.Equal("authorization_pending", await PollErrorAsync(api, started.device_code));
    }

    [PostgresFact]
    public async Task RemovingADevice_InSettings_SignsItOutAtOnce()
    {
        await using DeviceApi api = await StartAsync();
        Guid user = await api.AddUserAsync();
        Guid stranger = await api.AddUserAsync("stranger");
        string token = await api.SignInDeviceAsync(user, "Gaming PC");
        await api.SignInDeviceAsync(user, "Laptop");

        using HttpResponseMessage listed = await api.SendAsync(HttpMethod.Get, "/api/me/devices", cookieUser: user);
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        using JsonDocument list = JsonDocument.Parse(await listed.Content.ReadAsStringAsync());
        Assert.Equal(["Laptop", "Gaming PC"],
            list.RootElement.EnumerateArray().Select(device => device.GetProperty("name").GetString()));
        JsonElement gamingPc = list.RootElement[1];
        foreach (string field in new[] { "id", "created_at", "last_used_at" })
        {
            Assert.True(gamingPc.TryGetProperty(field, out _), field);
        }

        string id = gamingPc.GetProperty("id").GetString()!;
        using HttpResponseMessage strangers = await api.SendAsync(HttpMethod.Delete, $"/api/me/devices/{id}",
            cookieUser: stranger);
        Assert.Equal(HttpStatusCode.NotFound, strangers.StatusCode);
        using (HttpResponseMessage stillWorks = await api.SendAsync(HttpMethod.Get, "/api/games/categories",
                   deviceToken: token))
        {
            Assert.Equal(HttpStatusCode.OK, stillWorks.StatusCode);
        }

        using HttpResponseMessage removed = await api.SendAsync(HttpMethod.Delete, $"/api/me/devices/{id}",
            cookieUser: user);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        using HttpResponseMessage refused = await api.SendAsync(HttpMethod.Get, "/api/games/categories",
            deviceToken: token);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        using HttpResponseMessage again = await api.SendAsync(HttpMethod.Delete, $"/api/me/devices/{id}",
            cookieUser: user);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        using HttpResponseMessage remaining = await api.SendAsync(HttpMethod.Get, "/api/me/devices", cookieUser: user);
        Assert.DoesNotContain("Gaming PC", await remaining.Content.ReadAsStringAsync());
    }

    [PostgresFact]
    public async Task SigningOutFromTheApp_RevokesItsOwnToken()
    {
        await using DeviceApi api = await StartAsync();
        Guid user = await api.AddUserAsync();
        string token = await api.SignInDeviceAsync(user);
        string other = await api.SignInDeviceAsync(user, "Laptop");

        using HttpResponseMessage cookie = await api.SendAsync(HttpMethod.Delete, "/api/devices/current",
            cookieUser: user);
        Assert.Equal(HttpStatusCode.Unauthorized, cookie.StatusCode);

        using HttpResponseMessage signOut = await api.SendAsync(HttpMethod.Delete, "/api/devices/current",
            deviceToken: token);
        Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);

        using HttpResponseMessage refused = await api.SendAsync(HttpMethod.Get, "/api/me/plan", deviceToken: token);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        using HttpResponseMessage otherWorks = await api.SendAsync(HttpMethod.Get, "/api/me/plan", deviceToken: other);
        Assert.Equal(HttpStatusCode.OK, otherWorks.StatusCode);
    }

    [PostgresFact]
    public async Task ASuspendedAccountsToken_IsRefused()
    {
        await using DeviceApi api = await StartAsync();
        Guid user = await api.AddUserAsync();
        string token = await api.SignInDeviceAsync(user);
        await api.Connection.ExecuteAsync("UPDATE app_user SET suspended_at = now() WHERE id = @user", new { user });

        using HttpResponseMessage books = await api.SendAsync(HttpMethod.Get, "/api/games/categories",
            deviceToken: token);
        using HttpResponseMessage plan = await api.SendAsync(HttpMethod.Get, "/api/me/plan", deviceToken: token);

        Assert.Equal(HttpStatusCode.Unauthorized, books.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, plan.StatusCode);
    }

    [PostgresFact]
    public async Task ADeviceToken_IsRefusedEverywhereItIsntListed()
    {
        await using DeviceApi api = await StartAsync();
        Guid user = await api.AddUserAsync();
        Guid book = await api.AddBookAsync(user, "Valorant");
        string token = await api.SignInDeviceAsync(user);

        (HttpMethod Method, string Path, object? Json)[] unlisted =
        [
            (HttpMethod.Get, "/api/clips", null),
            (HttpMethod.Get, "/api/me", null),
            (HttpMethod.Put, "/api/me/email", new { email = "new@example.test" }),
            (HttpMethod.Get, "/api/me/devices", null),
            (HttpMethod.Get, $"/api/games/categories/{book}", null),
            (HttpMethod.Get, "/api/devices/authorize/BCDFGHJK", null),
            (HttpMethod.Post, "/api/clips/bin", new { clip_ids = Array.Empty<Guid>() })
        ];
        foreach ((HttpMethod method, string path, object? json) in unlisted)
        {
            using HttpResponseMessage withToken = await api.SendAsync(method, path, deviceToken: token, json: json);
            Assert.True(withToken.StatusCode == HttpStatusCode.Unauthorized, $"{method} {path}: {withToken.StatusCode}");
        }

        using HttpResponseMessage clipsWithCookie = await api.SendAsync(HttpMethod.Get, "/api/clips", cookieUser: user);
        Assert.Equal(HttpStatusCode.OK, clipsWithCookie.StatusCode);
    }

    [PostgresFact]
    public async Task TheListedEndpoints_TakeTheCookie_AndADeviceToken()
    {
        await using DeviceApi api = await StartAsync();
        Guid user = await api.AddUserAsync();
        Guid book = await api.AddBookAsync(user, "Apex Legends");
        string token = await api.SignInDeviceAsync(user);

        foreach ((Guid? cookie, string? bearer, string title) in new (Guid?, string?, string)[]
                 {
                     (user, null, "from-browser"),
                     (null, token, "from-uploader")
                 })
        {
            using HttpResponseMessage books = await api.SendAsync(HttpMethod.Get, "/api/games/categories",
                cookieUser: cookie, deviceToken: bearer);
            Assert.Equal(HttpStatusCode.OK, books.StatusCode);
            using HttpResponseMessage plan = await api.SendAsync(HttpMethod.Get, "/api/me/plan",
                cookieUser: cookie, deviceToken: bearer);
            Assert.Equal(HttpStatusCode.OK, plan.StatusCode);

            using HttpResponseMessage prepared = await api.SendAsync(HttpMethod.Post,
                $"/api/clips/categories/{book}/videos?videoTitle={title}&fileSize=1048576&md5Hash={Guid.NewGuid():N}",
                cookieUser: cookie, deviceToken: bearer);
            Assert.Equal(HttpStatusCode.OK, prepared.StatusCode);
            using JsonDocument clip = JsonDocument.Parse(await prepared.Content.ReadAsStringAsync());
            JsonElement upload = clip.RootElement.GetProperty("upload");
            Assert.Equal("s3-multipart", upload.GetProperty("protocol").GetString());
            string uploadId = upload.GetProperty("multipart").GetProperty("upload_id").GetString()!;
            string clipId = clip.RootElement.GetProperty("clip_id").GetString()!;

            using HttpResponseMessage parts = await api.SendAsync(HttpMethod.Post,
                $"/api/clips/videos/{clipId}/upload/parts", cookieUser: cookie, deviceToken: bearer,
                json: new { upload_id = uploadId, part_numbers = new[] { 1 } });
            Assert.Equal(HttpStatusCode.OK, parts.StatusCode);
            using HttpResponseMessage listed = await api.SendAsync(HttpMethod.Get,
                $"/api/clips/videos/{clipId}/upload/parts?uploadId={uploadId}", cookieUser: cookie,
                deviceToken: bearer);
            Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
            // Nothing was uploaded, so completing is refused, but by the upload's own checks, not for want of a sign-in.
            using HttpResponseMessage complete = await api.SendAsync(HttpMethod.Post,
                $"/api/clips/videos/{clipId}/upload/complete", cookieUser: cookie, deviceToken: bearer,
                json: new { upload_id = uploadId, parts = Array.Empty<object>() });
            Assert.NotEqual(HttpStatusCode.Unauthorized, complete.StatusCode);
            Assert.NotEqual(HttpStatusCode.Forbidden, complete.StatusCode);

            // Cancelling: the clip is still awaiting its upload, so either may delete it.
            using HttpResponseMessage cancelled = await api.SendAsync(HttpMethod.Delete, $"/api/clips/videos/{clipId}",
                cookieUser: cookie, deviceToken: bearer);
            Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        }
    }

    [PostgresFact]
    public async Task ADeviceToken_CantDeleteAClipThatHasUploaded()
    {
        await using DeviceApi api = await StartAsync();
        Guid user = await api.AddUserAsync();
        Guid stranger = await api.AddUserAsync("stranger");
        Guid book = await api.AddBookAsync(user, "Apex Legends");
        string token = await api.SignInDeviceAsync(user);
        Guid ready = await api.AddClipAsync(user, book, "ready");
        Guid uploaded = await api.AddClipAsync(user, book, "uploaded");
        Guid strangers = await api.AddClipAsync(stranger, await api.AddBookAsync(stranger, "Valorant"),
            "awaiting_upload");

        using HttpResponseMessage readyByToken = await api.SendAsync(HttpMethod.Delete, $"/api/clips/videos/{ready}",
            deviceToken: token);
        using HttpResponseMessage uploadedByToken = await api.SendAsync(HttpMethod.Delete,
            $"/api/clips/videos/{uploaded}", deviceToken: token);
        using HttpResponseMessage strangersByToken = await api.SendAsync(HttpMethod.Delete,
            $"/api/clips/videos/{strangers}", deviceToken: token);
        Assert.Equal(HttpStatusCode.Forbidden, readyByToken.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, uploadedByToken.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, strangersByToken.StatusCode);
        Assert.Equal(3, await api.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM clip"));

        using HttpResponseMessage readyByCookie = await api.SendAsync(HttpMethod.Delete, $"/api/clips/videos/{ready}",
            cookieUser: user);
        Assert.Equal(HttpStatusCode.OK, readyByCookie.StatusCode);
    }

    [PostgresFact]
    public async Task TheVersionGate_RefusesOnlyOlderUploaders()
    {
        await using DeviceApi api = await StartAsync(new Dictionary<string, string?>
        {
            ["Uploader:MinimumVersion"] = "1.2.0"
        });
        Guid user = await api.AddUserAsync();
        string token = await api.SignInDeviceAsync(user);

        using HttpResponseMessage old = await api.SendAsync(HttpMethod.Get, "/api/me/plan", deviceToken: token,
            client: "uploader/1.1.9");
        Assert.Equal((HttpStatusCode)426, old.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await old.Content.ReadAsStringAsync());
        Assert.Equal("client_too_old", body.RootElement.GetProperty("error").GetString());
        Assert.Equal("1.2.0", body.RootElement.GetProperty("minimum_version").GetString());

        // Even before signing in, so an old app is told to update instead of failing to sign in.
        using HttpResponseMessage authorize = await api.SendAsync(HttpMethod.Post, "/api/devices/authorize",
            json: new { device_name = "Gaming PC" }, client: "uploader/1.0.0");
        Assert.Equal((HttpStatusCode)426, authorize.StatusCode);

        using HttpResponseMessage current = await api.SendAsync(HttpMethod.Get, "/api/me/plan", deviceToken: token,
            client: "uploader/1.2.0");
        using HttpResponseMessage newer = await api.SendAsync(HttpMethod.Get, "/api/me/plan", deviceToken: token,
            client: "uploader/1.10.0");
        using HttpResponseMessage browser = await api.SendAsync(HttpMethod.Get, "/api/me/plan", cookieUser: user);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newer.StatusCode);
        Assert.Equal(HttpStatusCode.OK, browser.StatusCode);
    }

    [PostgresFact]
    public async Task ATokensUse_IsRecorded_AtMostOnceAnHour()
    {
        await using DeviceApi api = await StartAsync();
        Guid user = await api.AddUserAsync();
        string token = await api.SignInDeviceAsync(user);
        await api.Connection.ExecuteAsync("UPDATE device_token SET last_used_at = now() - interval '2 hours'");

        using (await api.SendAsync(HttpMethod.Get, "/api/me/plan", deviceToken: token, client: "uploader/1.0.3"))
        {
        }

        (DateTime lastUsed, string? version) = await api.Connection
            .QuerySingleAsync<(DateTime, string?)>("SELECT last_used_at, client_version FROM device_token");
        Assert.True(lastUsed > DateTime.UtcNow.AddMinutes(-5));
        Assert.Equal("uploader/1.0.3", version);

        using (await api.SendAsync(HttpMethod.Get, "/api/me/plan", deviceToken: token, client: "uploader/1.0.4"))
        {
        }

        Assert.Equal("uploader/1.0.3",
            await api.Connection.ExecuteScalarAsync<string>("SELECT client_version FROM device_token"));
    }

    [PostgresFact]
    public async Task TheSweep_DeletesExpiredRequests_AndRevokesUnusedTokens()
    {
        await using DeviceApi api = await StartAsync();
        Guid user = await api.AddUserAsync();
        string stale = await api.SignInDeviceAsync(user, "Old laptop");
        string fresh = await api.SignInDeviceAsync(user, "Gaming PC");
        await api.Connection.ExecuteAsync(
            "UPDATE device_token SET last_used_at = now() - interval '91 days' WHERE name = 'Old laptop'");
        DeviceAuthorizationJson waiting = await AuthorizeAsync(api);
        await AuthorizeAsync(api);
        await api.Connection.ExecuteAsync("""
            UPDATE device_authorization SET expires_at = now() - interval '1 minute'
            WHERE user_code <> @code
            """, new { code = waiting.user_code.Replace("-", "") });

        await using (AsyncServiceScope scope = api.Services.CreateAsyncScope())
        {
            (int expired, int revoked) = await scope.ServiceProvider.GetRequiredService<DeviceTokenSweeper>()
                .SweepAsync();
            Assert.Equal(1, expired);
            Assert.Equal(1, revoked);
        }

        Assert.Equal(1, await api.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM device_authorization"));
        using HttpResponseMessage staleUse = await api.SendAsync(HttpMethod.Get, "/api/me/plan", deviceToken: stale);
        using HttpResponseMessage freshUse = await api.SendAsync(HttpMethod.Get, "/api/me/plan", deviceToken: fresh);
        Assert.Equal(HttpStatusCode.Unauthorized, staleUse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, freshUse.StatusCode);
    }

    private static async Task<DeviceAuthorizationJson> AuthorizeAsync(DeviceApi api)
    {
        using HttpResponseMessage authorize = await api.SendAsync(HttpMethod.Post, "/api/devices/authorize",
            json: new { device_name = "Gaming PC" });
        authorize.EnsureSuccessStatusCode();
        return (await authorize.Content.ReadFromJsonAsync<DeviceAuthorizationJson>())!;
    }

    private static async Task<string> PollErrorAsync(DeviceApi api, string deviceCode)
    {
        using HttpResponseMessage response = await api.SendAsync(HttpMethod.Post, "/api/devices/token",
            json: new { device_code = deviceCode });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DeviceTokenErrorJson>())!.error;
    }

    /// <summary>Moves the last poll back past the interval, instead of waiting it out.</summary>
    private static Task PassPollIntervalAsync(DeviceApi api) =>
        api.Connection.ExecuteAsync("UPDATE device_authorization SET last_polled_at = now() - interval '1 minute'");
}
