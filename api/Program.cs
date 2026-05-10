using Reelshelf;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddReelshelfApi();

WebApplication app = builder.Build();
app.UseReelshelfApi();
app.MapReelshelfApi();

app.Run();

// Make Program class public for test access (in global namespace for WebApplicationFactory)
public partial class Program { }
