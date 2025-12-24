using Gam.Core;
using Gam.Core.Abstractions;
using Gam.Core.Models;
using Gam.Core.Tools;
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

// Add tool handler
builder.Services.AddSingleton<GamToolHandler>();

// Add OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ============================================================================
// Standard REST Endpoints
// ============================================================================

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
        Query = dto.Query,
        Options = dto.MaxTokens.HasValue 
            ? new ResearchOptions { MaxContextTokens = dto.MaxTokens.Value }
            : null
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

// ============================================================================
// AI SDK / Tool Calling Endpoints
// ============================================================================

// Get tool schemas (OpenAI function calling format)
app.MapGet("/tools", () => Results.Ok(GamToolSchemas.GetAllTools()))
    .WithName("GetTools")
    .WithDescription("Get GAM tool definitions in OpenAI function calling format")
    .WithOpenApi();

// Execute a tool call (for AI SDK integration)
app.MapPost("/tools/execute", async (ToolCallDto dto, GamToolHandler handler) =>
{
    var result = await handler.ExecuteAsync(dto.Name, dto.Arguments);
    return result.Success 
        ? Results.Ok(result) 
        : Results.BadRequest(result);
})
.WithName("ExecuteTool")
.WithDescription("Execute a GAM tool call with JSON arguments")
.WithOpenApi();

// ============================================================================
// Vercel AI SDK Compatible Endpoint
// ============================================================================

// This endpoint format works with Vercel AI SDK's tool calling
app.MapPost("/v1/tools/{toolName}", async (string toolName, ToolArgumentsDto dto, GamToolHandler handler) =>
{
    var result = await handler.ExecuteAsync($"gam_{toolName}", dto.ToJson());
    return result.Success 
        ? Results.Ok(new { role = "tool", content = result.Content, metadata = result.Metadata })
        : Results.BadRequest(new { error = result.Error });
})
.WithName("ExecuteToolByName")
.WithDescription("Execute a GAM tool by name (Vercel AI SDK compatible)")
.WithOpenApi();

app.Run();

// ============================================================================
// DTOs
// ============================================================================

record MemorizeDto(string OwnerId, string UserMessage, string AssistantMessage);
record ResearchDto(string OwnerId, string Query, int? MaxTokens = null);
record ForgetDto(string OwnerId, bool All = false);
record ToolCallDto(string Name, string Arguments);

record ToolArgumentsDto(
    string? OwnerId, 
    string? Query, 
    string? UserMessage, 
    string? AssistantMessage,
    int? MaxTokens,
    bool? All,
    string? Before)
{
    public string ToJson() => System.Text.Json.JsonSerializer.Serialize(this, 
        new System.Text.Json.JsonSerializerOptions 
        { 
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
}
