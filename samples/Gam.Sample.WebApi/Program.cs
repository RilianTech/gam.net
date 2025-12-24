using Gam.Core;
using Gam.Core.Abstractions;
using Gam.Core.Models;
using Gam.Providers.OpenAI;
using Gam.Storage.Postgres;

var builder = WebApplication.CreateBuilder(args);

// Add GAM services
builder.Services.AddGamCore();

// Add PostgreSQL storage
var connectionString = builder.Configuration.GetConnectionString("Postgres") 
    ?? "Host=localhost;Database=gam;Username=postgres;Password=postgres";
builder.Services.AddGamPostgresStorage(connectionString);

// Add OpenAI provider
var apiKey = builder.Configuration["OpenAI:ApiKey"]
    ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
    ?? throw new InvalidOperationException("OpenAI API key is required");
builder.Services.AddGamOpenAI(apiKey);

// Add OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("Health")
    .WithOpenApi();

// Memorize endpoint
app.MapPost("/memorize", async (MemorizeDto dto, IGamService gam) =>
{
    await gam.MemorizeAsync(new MemorizeRequest
    {
        Turn = new ConversationTurn
        {
            OwnerId = dto.OwnerId,
            UserMessage = dto.UserMessage,
            AssistantMessage = dto.AssistantMessage,
            Timestamp = DateTimeOffset.UtcNow
        }
    });
    return Results.Ok(new { success = true });
})
.WithName("Memorize")
.WithOpenApi();

// Research endpoint
app.MapPost("/research", async (ResearchDto dto, IGamService gam) =>
{
    var context = await gam.ResearchAsync(new ResearchRequest
    {
        OwnerId = dto.OwnerId,
        Query = dto.Query
    });
    
    return Results.Ok(new
    {
        pageCount = context.Pages.Count,
        totalTokens = context.TotalTokens,
        iterations = context.IterationsPerformed,
        durationMs = context.Duration.TotalMilliseconds,
        formattedContext = context.FormatForPrompt(),
        pages = context.Pages.Select(p => new
        {
            p.PageId,
            p.Content,
            p.TokenCount,
            p.RelevanceScore,
            p.RetrievedBy,
            p.CreatedAt
        })
    });
})
.WithName("Research")
.WithOpenApi();

// Forget endpoint
app.MapPost("/forget", async (ForgetDto dto, IGamService gam) =>
{
    await gam.ForgetAsync(new ForgetRequest
    {
        OwnerId = dto.OwnerId,
        All = dto.All
    });
    return Results.Ok(new { success = true });
})
.WithName("Forget")
.WithOpenApi();

app.Run();

// DTOs
record MemorizeDto(string OwnerId, string UserMessage, string AssistantMessage);
record ResearchDto(string OwnerId, string Query);
record ForgetDto(string OwnerId, bool All = false);
