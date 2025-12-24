using Gam.Core;
using Gam.Core.Abstractions;
using Gam.Core.Models;
using Gam.Providers.OpenAI;
using Gam.Storage.Postgres;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Build configuration
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .Build();

// Configure services
var services = new ServiceCollection();

services.AddLogging(builder => builder
    .AddConsole()
    .SetMinimumLevel(LogLevel.Information));

// Add GAM services
services.AddGamCore();

// Add PostgreSQL storage
var connectionString = configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required in appsettings.json");
services.AddGamPostgresStorage(connectionString);

// Add OpenAI provider
var apiKey = configuration["OpenAI:ApiKey"];
if (string.IsNullOrWhiteSpace(apiKey))
    throw new InvalidOperationException("OpenAI:ApiKey is required in appsettings.json or environment variable OpenAI__ApiKey");
services.AddGamOpenAI(apiKey);

var provider = services.BuildServiceProvider();
var gam = provider.GetRequiredService<IGamService>();
var logger = provider.GetRequiredService<ILogger<Program>>();

Console.WriteLine("GAM.NET Console Sample");
Console.WriteLine("======================");
Console.WriteLine();

// Example: Store a memory
Console.WriteLine("Storing a memory...");
await gam.MemorizeAsync(new MemorizeRequest
{
    Turn = new ConversationTurn
    {
        OwnerId = "demo-user",
        UserMessage = "How do I configure Kubernetes health checks?",
        AssistantMessage = """
            You can configure liveness and readiness probes in your pod spec:
            
            ```yaml
            livenessProbe:
              httpGet:
                path: /healthz
                port: 8080
              initialDelaySeconds: 3
              periodSeconds: 3
            readinessProbe:
              httpGet:
                path: /ready
                port: 8080
              initialDelaySeconds: 3
              periodSeconds: 3
            ```
            
            Liveness probes detect if your app is running. Readiness probes detect if it can accept traffic.
            """,
        Timestamp = DateTimeOffset.UtcNow
    }
});
Console.WriteLine("Memory stored!");
Console.WriteLine();

// Example: Research memories
Console.WriteLine("Researching memories...");
var context = await gam.ResearchAsync(new ResearchRequest
{
    OwnerId = "demo-user",
    Query = "What did we discuss about Kubernetes?"
});

Console.WriteLine($"Found {context.Pages.Count} relevant memories in {context.Duration.TotalMilliseconds:F0}ms");
Console.WriteLine($"Total tokens: {context.TotalTokens}");
Console.WriteLine($"Iterations: {context.IterationsPerformed}");
Console.WriteLine();

if (context.Pages.Count > 0)
{
    Console.WriteLine("Memory context for LLM prompt:");
    Console.WriteLine("------------------------------");
    Console.WriteLine(context.FormatForPrompt());
}

Console.WriteLine("Done!");
Console.WriteLine();

// Run comprehensive tests
Console.WriteLine("Running comprehensive tests...");
Console.WriteLine();
var comprehensiveTest = new Gam.Sample.Console.ComprehensiveTest(gam, logger);
await comprehensiveTest.RunAllTestsAsync();

public partial class Program { }
