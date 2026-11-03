using System.Net;
using System.Net.Http.Headers;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Reelshelf.Core;
using Reelshelf.Games;
using Reelshelf.Test.Video;
using Xunit;

namespace Reelshelf.Test.Games;

/// <summary>Picking categories due a refresh and copying their images, against a real Postgres on V56.</summary>
public class GameArtMirrorTests
{
    private const string CoverA = "https://images.igdb.com/igdb/image/upload/t_cover_big/coaaa.jpg";
    private const string CoverB = "https://images.igdb.com/igdb/image/upload/t_cover_big/cobbb.jpg";
    private const string Logo = "https://images.igdb.com/igdb/image/upload/t_1080p/arlogo.png";

    [PostgresFact]
    public async Task DueCategories_AreRefreshedAndCopied_AndAChangedImageReplacesItsCopy()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        string schema = $"game_art_test_{Guid.NewGuid():N}";
        await using (NpgsqlConnection admin = new(PostgresFactAttribute.ConnectionString))
        {
            await admin.OpenAsync();
            await admin.ExecuteAsync($"CREATE SCHEMA {schema}");
        }

        await using NpgsqlDataSource dataSource = ReelshelfDataSource.Create(
            $"{PostgresFactAttribute.ConnectionString};Search Path={schema}");
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
        try
        {
            await connection.ExecuteAsync("""
                CREATE TABLE game_category (
                    id uuid PRIMARY KEY,
                    igdb_id bigint,
                    name text NOT NULL,
                    slug text NOT NULL UNIQUE,
                    cover_url text,
                    key_art_url text,
                    game_logo_url text,
                    cloth_color text,
                    is_custom boolean NOT NULL DEFAULT false,
                    created_at timestamptz NOT NULL DEFAULT now(),
                    updated_at timestamptz NOT NULL DEFAULT now()
                );
                CREATE TABLE user_game_category (
                    user_id uuid NOT NULL,
                    game_category_id uuid NOT NULL REFERENCES game_category(id) ON DELETE CASCADE,
                    PRIMARY KEY (user_id, game_category_id)
                );
                """);
            await TestClipSchema.CreateAsync(connection);
            await connection.ExecuteAsync(
                await File.ReadAllTextAsync(TestClipSchema.MigrationPath("V56__game_category_art_copies.sql")));
            await RunAsync(connection, new GameCategoryStatements(dataSource.CreateConnection()));
        }
        finally
        {
            await connection.ExecuteAsync($"DROP SCHEMA {schema} CASCADE");
        }
    }

    private static async Task RunAsync(NpgsqlConnection connection, GameCategoryStatements statements)
    {
        Guid owner = Guid.NewGuid();
        Guid shelved = await AddCategory(connection, "Apex Legends", 1, CoverA);
        Guid withClip = await AddCategory(connection, "Celeste", 2, CoverA);
        Guid unused = await AddCategory(connection, "Doom", 3, CoverA);
        Guid custom = await AddCategory(connection, "My game", null, "https://example.com/c.png");
        await statements.AddUserCategoryAsync(owner, shelved);
        await statements.AddUserCategoryAsync(owner, custom);
        await connection.ExecuteAsync(
            "INSERT INTO clip (id, owner_id, video_id, game_category_id) VALUES (@id, @owner, @id, @withClip)",
            new { id = Guid.NewGuid(), owner, withClip });

        // Only IGDB books someone has are due, never read ones first.
        Assert.Equal(new[] { shelved, withClip }.Order(), (await Due(statements, false)).Order());

        // Reading IGDB marks it checked; without a copy step it is then not due for a day.
        await statements.UpdateIgdbAssetsAsync(shelved,
            new GameDetails(1, "Apex Legends", "apex", CoverA, null, Logo, [], [], null));
        Assert.Equal([withClip], await Due(statements, false));
        // With copies wanted, its uncopied images keep it due.
        Assert.Contains(shelved, await Due(statements, true));

        FakeStore store = new();
        GameArtMirror mirror = new(store, statements, new FakeHttpClientFactory(), NullLogger<GameArtMirror>.Instance);

        GameCategory apex = (await statements.GetByIdAsync(shelved))!;
        Assert.True(await mirror.CopyAsync(apex, CancellationToken.None));
        apex = (await statements.GetByIdAsync(shelved))!;
        string coverKey = GameArtKeys.For(shelved, GameArtKind.Cover, CoverA)!;
        Assert.Equal(coverKey, apex.CoverKey);
        Assert.Equal(GameArtKeys.For(shelved, GameArtKind.Logo, Logo), apex.GameLogoKey);
        Assert.Null(apex.KeyArtKey);
        Assert.Equal("image/jpeg", store.Objects[coverKey]);
        Assert.Equal("image/png", store.Objects[apex.GameLogoKey!]);
        Assert.DoesNotContain(shelved, await Due(statements, true));

        // A second pass copies nothing again.
        int puts = store.Puts;
        Assert.True(await mirror.CopyAsync(apex, CancellationToken.None));
        Assert.Equal(puts, store.Puts);

        // IGDB's cover changes: the new one is copied and the old copy deleted.
        apex = await statements.UpdateIgdbAssetsAsync(shelved,
            new GameDetails(1, "Apex Legends", "apex", CoverB, null, null, [], [], null));
        Assert.Equal(Logo, apex.GameLogoUrl);
        Assert.True(await mirror.CopyAsync(apex, CancellationToken.None));
        apex = (await statements.GetByIdAsync(shelved))!;
        Assert.Equal(GameArtKeys.For(shelved, GameArtKind.Cover, CoverB), apex.CoverKey);
        Assert.DoesNotContain(coverKey, store.Objects.Keys);

        // A copy made of an image that changed meanwhile isn't recorded.
        (bool recorded, _) = await statements.SetArtKeyAsync(shelved, GameArtKind.Cover, CoverA, coverKey);
        Assert.False(recorded);
    }

    private static async Task<List<Guid>> Due(GameCategoryStatements statements, bool includeUncopied) =>
        (await statements.GetCategoriesNeedingIgdbAssetRefreshAsync(10, TimeSpan.FromHours(24), includeUncopied))
        .Select(category => category.Id).ToList();

    private static async Task<Guid> AddCategory(NpgsqlConnection connection, string name, long? igdbId, string cover)
    {
        Guid id = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO game_category (id, igdb_id, name, slug, cover_url) VALUES (@id, @igdbId, @name, @slug, @cover)",
            new { id, igdbId, name, slug = $"{name.ToLowerInvariant().Replace(' ', '-')}-{id:N}", cover });
        return id;
    }

    private sealed class FakeStore : IGameArtStore
    {
        public Dictionary<string, string> Objects { get; } = [];
        public int Puts { get; private set; }

        public Task PutAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken)
        {
            Puts++;
            Objects[key] = contentType;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string key)
        {
            Objects.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new ImageHandler());

        private sealed class ImageHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                ByteArrayContent content = new([1, 2, 3]);
                content.Headers.ContentType = new MediaTypeHeaderValue(
                    request.RequestUri!.AbsolutePath.EndsWith(".png") ? "image/png" : "image/jpeg");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            }
        }
    }
}
