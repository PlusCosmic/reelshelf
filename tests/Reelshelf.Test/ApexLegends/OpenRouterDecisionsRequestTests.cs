using System.Net;
using System.Text;
using System.Text.Json;
using FFMpegCore;
using FFMpegCore.Pipes;
using Reelshelf.ApexLegends.LegendDetection;
using Xunit;

namespace Reelshelf.Test.ApexLegends;

/// <summary>
/// Runs the OpenRouter Decisions recognizer against canned replies, pinning the request it sends (prompt, the sheet
/// in halves, every screenshot fitted to Jev's image limits, both questions with every legend as a choice) and how
/// answers become a result. Splitting the sheet and fitting the screenshots run the real ffmpeg, which CI installs.
/// </summary>
public class OpenRouterDecisionsRequestTests
{
    private static string Response(double hud, string choice, double choiceProbability)
    {
        return $$"""
            {
              "answers": {
                "hud_detected": { "type": "noul", "noul": {{hud}} },
                "legend": {
                  "type": "choice",
                  "choice": "{{choice}}",
                  "probabilities": { "{{choice}}": {{choiceProbability}}, "unidentifiable": 0.01 },
                  "confidence": 0.5
                }
              },
              "id": "gen-dec-1",
              "model": "typesafe/jev-1.14-20261014",
              "provider": "TypeSafe",
              "usage": { "input_tokens": 9000, "output_tokens": 0, "cost": 0.0004 }
            }
            """;
    }

    [Fact]
    public async Task SendsStateAndQuestions_AndReadsTheAnswers()
    {
        RecordingHandler handler = new(Response(0.97, "Horizon", 0.93));
        LegendDetectionResources resources = new();

        // A 1080p frame's close-up fits as it is; a 1440p frame's is too wide and a 480p frame's too short.
        LegendRecognition recognition = await Recognizer(handler, resources).RecognizeAsync(
            [
                new LegendScreenshot(await JpegAsync(960, 540), await JpegAsync(1348, 552)),
                new LegendScreenshot(await JpegAsync(1280, 720), await JpegAsync(1798, 734)),
                new LegendScreenshot(await JpegAsync(854, 480), await JpegAsync(600, 246))
            ],
            CancellationToken.None);

        Assert.Equal("https://openrouter.ai/api/alpha/decisions", handler.RequestUri!.ToString());
        Assert.Equal("Bearer test-key", handler.Authorization);
        JsonElement request = handler.RequestBody!.Value;
        Assert.Equal("typesafe/jev-1.14", request.GetProperty("model").GetString());

        List<JsonElement> state = request.GetProperty("state").EnumerateArray().ToList();
        Assert.Equal(resources.DecisionsPrompt, state[0].GetString());
        List<string> images = state
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => item.GetProperty("image_url").GetProperty("url").GetString()!)
            .ToList();
        Assert.Equal(8, images.Count);
        Assert.StartsWith("data:image/png;base64,", images[0]);
        Assert.StartsWith("data:image/png;base64,", images[1]);
        List<(int Width, int Height)> sizes = [];
        foreach (string image in images.Skip(2))
        {
            Assert.StartsWith("data:image/jpeg;base64,", image);
            IMediaAnalysis analysis = await FFProbe.AnalyseAsync(
                new MemoryStream(Convert.FromBase64String(image["data:image/jpeg;base64,".Length..])));
            sizes.Add((analysis.PrimaryVideoStream!.Width, analysis.PrimaryVideoStream.Height));
        }

        Assert.All(sizes, size =>
        {
            Assert.InRange(size.Width, 256, 1568);
            Assert.InRange(size.Height, 256, 1568);
            Assert.InRange(size.Width * size.Height, 78_400, 2_458_624);
        });
        Assert.Equal((960, 540), sizes[0]);
        Assert.Equal((1348, 552), sizes[1]);
        Assert.Equal(1568, sizes[3].Width);
        Assert.Equal(256, sizes[5].Height);
        Assert.Contains(state, item => item.ValueKind == JsonValueKind.String
                                       && item.GetString() == "Screenshot 3 of 3, close-up of the bottom-left HUD panel:");

        JsonElement questions = request.GetProperty("questions");
        Assert.Equal("noul", questions.GetProperty("hud_detected").GetProperty("type").GetString());
        JsonElement legend = questions.GetProperty("legend");
        Assert.Equal("choice", legend.GetProperty("type").GetString());
        Assert.Equal([.. ApexLegendNames.All, "unidentifiable"],
            legend.GetProperty("criteria").EnumerateObject().Select(choice => choice.Name).ToList());

        Assert.True(recognition.Result.HudDetected);
        Assert.Equal("Horizon", recognition.Result.Player.Legend);
        Assert.Equal(0.93, recognition.Result.Player.LegendConfidence);
        Assert.Equal(9000, recognition.InputTokens);
        Assert.Null(recognition.CachedInputTokens);
        Assert.Equal(0, recognition.OutputTokens);
        Assert.Equal(0.0004m, recognition.CostUsd);
    }

    [Fact]
    public void Unidentifiable_IsNoLegend()
    {
        LegendRecognition recognition = OpenRouterDecisionsLegendRecognizer.Read(Response(0.2, "unidentifiable", 0.88));

        Assert.False(recognition.Result.HudDetected);
        Assert.Null(recognition.Result.Player.Legend);
        Assert.Equal(0, recognition.Result.Player.LegendConfidence);
    }

    [Fact]
    public void ChoiceOffTheSheet_IsDropped()
    {
        Assert.Null(OpenRouterDecisionsLegendRecognizer.Read(Response(0.9, "Kraber", 0.9)).Result.Player.Legend);
    }

    [Fact]
    public async Task MissingAnswer_FailsWithTheRawResponse()
    {
        const string missing = """{ "answers": { "hud_detected": { "type": "noul", "noul": 0.9 } } }""";
        List<LegendScreenshot> images = await ImagesAsync();

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Recognizer(new RecordingHandler(missing), new LegendDetectionResources())
                .RecognizeAsync(images, CancellationToken.None));

        Assert.Contains("no answer for legend", ex.Message);
        Assert.Equal(missing, ex.Data["RawResponse"]);
    }

    [Fact]
    public async Task ErrorStatus_Throws()
    {
        RecordingHandler handler = new("""{ "error": { "code": 413, "message": "image too large" } }""",
            HttpStatusCode.RequestEntityTooLarge);
        List<LegendScreenshot> images = await ImagesAsync();

        HttpRequestException ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            Recognizer(handler, new LegendDetectionResources())
                .RecognizeAsync(images, CancellationToken.None));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, ex.StatusCode);
        Assert.Contains("image too large", ex.Message);
    }

    [Fact]
    public void PromptVersion_DiffersFromOpenAIDecisions()
    {
        LegendDetectionResources resources = new();
        OpenRouterDecisionsLegendRecognizer recognizer = Recognizer(new RecordingHandler("{}"), resources);

        Assert.Equal(resources.OpenRouterDecisionsPromptVersion, recognizer.PromptVersion);
        Assert.NotEqual(resources.DecisionsPromptVersion, recognizer.PromptVersion);
        Assert.Equal(12, recognizer.PromptVersion.Length);
    }

    private static OpenRouterDecisionsLegendRecognizer Recognizer(
        RecordingHandler handler,
        LegendDetectionResources resources)
    {
        return new OpenRouterDecisionsLegendRecognizer(new HttpClient(handler), "test-key", "typesafe/jev-1.14",
            resources);
    }

    private static async Task<List<LegendScreenshot>> ImagesAsync()
    {
        return [new LegendScreenshot(await JpegAsync(960, 540), await JpegAsync(1348, 552))];
    }

    private static async Task<LegendFrame> JpegAsync(int width, int height)
    {
        using MemoryStream output = new();
        await FFMpegArguments
            .FromFileInput($"testsrc2=size={width}x{height}", verifyExists: false, options => options.ForceFormat("lavfi"))
            .OutputToPipe(new StreamPipeSink(output), options => options
                .WithCustomArgument("-frames:v 1")
                .WithVideoCodec("mjpeg")
                .ForceFormat("image2pipe"))
            .ProcessAsynchronously();
        return new LegendFrame(output.ToArray(), "image/jpeg");
    }

    private sealed class RecordingHandler(string responseJson, HttpStatusCode status = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? Authorization { get; private set; }
        public JsonElement? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            string body = await request.Content!.ReadAsStringAsync(cancellationToken);
            RequestBody = JsonDocument.Parse(body).RootElement.Clone();
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}
