using Gam.Core.Abstractions;
using Gam.Core.Models;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace Gam.Sample.Console;

public class ComprehensiveTest
{
    private readonly IGamService _gam;
    private readonly ILogger _logger;
    private readonly string _ownerId;

    public ComprehensiveTest(IGamService gam, ILogger logger)
    {
        _gam = gam;
        _logger = logger;
        _ownerId = $"test-user-{Guid.NewGuid().ToString("N")[..8]}";
    }

    public async Task RunAllTestsAsync()
    {
        System.Console.WriteLine($"Running comprehensive tests for owner: {_ownerId}");
        System.Console.WriteLine(new string('=', 60));
        System.Console.WriteLine();

        // Test 1: Multiple diverse conversations
        await TestMultipleConversationsAsync();

        // Test 2: Research with different query types
        await TestResearchQueriesAsync();

        // Test 3: Edge cases
        await TestEdgeCasesAsync();

        // Test 4: Token budget limits
        await TestTokenBudgetAsync();

        // Test 5: Cross-topic research
        await TestCrossTopicResearchAsync();

        System.Console.WriteLine();
        System.Console.WriteLine(new string('=', 60));
        System.Console.WriteLine("All tests completed!");
    }

    private async Task TestMultipleConversationsAsync()
    {
        System.Console.WriteLine("TEST 1: Storing multiple diverse conversations");
        System.Console.WriteLine(new string('-', 40));

        var conversations = new[]
        {
            ("What's the difference between Docker and Kubernetes?",
             """
             Docker is a containerization platform that packages applications and dependencies into containers.
             Kubernetes is a container orchestration platform that manages multiple containers across multiple hosts.
             
             Key differences:
             - Docker: Creates and runs containers
             - Kubernetes: Orchestrates and scales containers
             - Docker Compose: Simple multi-container on single host
             - Kubernetes: Complex multi-container across cluster
             """),

            ("How do I set up a CI/CD pipeline with GitHub Actions?",
             """
             Here's a basic GitHub Actions workflow:
             
             ```yaml
             name: CI/CD
             on: [push, pull_request]
             jobs:
               build:
                 runs-on: ubuntu-latest
                 steps:
                   - uses: actions/checkout@v4
                   - name: Build
                     run: dotnet build
                   - name: Test
                     run: dotnet test
             ```
             
             Key concepts:
             - Workflows are defined in .github/workflows/
             - Jobs run in parallel by default
             - Steps run sequentially within a job
             """),

            ("Explain async/await in C#",
             """
             Async/await is C#'s pattern for asynchronous programming:
             
             ```csharp
             public async Task<string> FetchDataAsync()
             {
                 var result = await httpClient.GetStringAsync(url);
                 return result;
             }
             ```
             
             Key points:
             - async marks a method as asynchronous
             - await pauses execution until the task completes
             - The thread is freed while waiting
             - Always return Task or Task<T> from async methods
             - Use ConfigureAwait(false) in library code
             """),

            ("What are the SOLID principles?",
             """
             SOLID is an acronym for five design principles:
             
             1. **S**ingle Responsibility: A class should have one reason to change
             2. **O**pen/Closed: Open for extension, closed for modification
             3. **L**iskov Substitution: Subtypes must be substitutable for base types
             4. **I**nterface Segregation: Many specific interfaces > one general interface
             5. **D**ependency Inversion: Depend on abstractions, not concretions
             
             These principles lead to maintainable, testable, and flexible code.
             """),

            ("How do I optimize PostgreSQL queries?",
             """
             PostgreSQL query optimization tips:
             
             1. **Use EXPLAIN ANALYZE** to understand query plans
             2. **Create appropriate indexes**:
                - B-tree for equality and range queries
                - GIN for full-text search and arrays
                - GiST for geometric data
             3. **Avoid SELECT ***: Only fetch needed columns
             4. **Use connection pooling** (PgBouncer)
             5. **Tune settings**: shared_buffers, work_mem, effective_cache_size
             6. **Vacuum regularly** to reclaim space and update statistics
             """),

            ("What is dependency injection and why use it?",
             """
             Dependency Injection (DI) is a design pattern where dependencies are provided to a class
             rather than created by it.
             
             ```csharp
             // Without DI - tightly coupled
             public class OrderService
             {
                 private readonly SqlDatabase _db = new SqlDatabase();
             }
             
             // With DI - loosely coupled
             public class OrderService
             {
                 private readonly IDatabase _db;
                 public OrderService(IDatabase db) => _db = db;
             }
             ```
             
             Benefits:
             - Easier testing (inject mocks)
             - Loose coupling
             - Configurable at runtime
             - Better separation of concerns
             """)
        };

        foreach (var (userMsg, assistantMsg) in conversations)
        {
            System.Console.WriteLine($"  Storing: {userMsg[..Math.Min(50, userMsg.Length)]}...");
            await _gam.MemorizeAsync(new MemorizeRequest
            {
                Turn = new ConversationTurn
                {
                    OwnerId = _ownerId,
                    UserMessage = userMsg,
                    AssistantMessage = assistantMsg,
                    Timestamp = DateTimeOffset.UtcNow
                }
            });
        }

        System.Console.WriteLine($"  Stored {conversations.Length} conversations");
        System.Console.WriteLine();
    }

    private async Task TestResearchQueriesAsync()
    {
        System.Console.WriteLine("TEST 2: Research with different query types");
        System.Console.WriteLine(new string('-', 40));

        var queries = new[]
        {
            "container orchestration",           // Should find Docker/K8s conversation
            "how to write async code in C#",     // Should find async/await conversation
            "database performance",              // Should find PostgreSQL conversation
            "design patterns for clean code",    // Should find SOLID + DI conversations
            "CI/CD automation",                  // Should find GitHub Actions conversation
            "testing and mocking",               // Should find DI conversation (mentions mocks)
        };

        foreach (var query in queries)
        {
            System.Console.WriteLine($"  Query: \"{query}\"");
            var context = await _gam.ResearchAsync(new ResearchRequest
            {
                OwnerId = _ownerId,
                Query = query
            });
            System.Console.WriteLine($"    Found: {context.Pages.Count} pages, {context.TotalTokens} tokens, {context.Duration.TotalMilliseconds:F0}ms");
            
            if (context.Pages.Count > 0)
            {
                System.Console.WriteLine($"    First match preview: {context.Pages[0].Content[..Math.Min(60, context.Pages[0].Content.Length)]}...");
            }
            System.Console.WriteLine();
        }
    }

    private async Task TestEdgeCasesAsync()
    {
        System.Console.WriteLine("TEST 3: Edge cases");
        System.Console.WriteLine(new string('-', 40));

        // Empty/nonsense query
        System.Console.WriteLine("  Testing empty-ish query...");
        var result1 = await _gam.ResearchAsync(new ResearchRequest
        {
            OwnerId = _ownerId,
            Query = "xyz123 gibberish nonexistent"
        });
        System.Console.WriteLine($"    Gibberish query: {result1.Pages.Count} pages found");

        // Very long conversation
        System.Console.WriteLine("  Testing long conversation storage...");
        var longContent = string.Join("\n", Enumerable.Range(1, 100).Select(i => $"Line {i}: This is a detailed explanation about topic {i} with various technical details."));
        await _gam.MemorizeAsync(new MemorizeRequest
        {
            Turn = new ConversationTurn
            {
                OwnerId = _ownerId,
                UserMessage = "Give me a very detailed explanation",
                AssistantMessage = longContent,
                Timestamp = DateTimeOffset.UtcNow
            }
        });
        System.Console.WriteLine("    Long conversation stored successfully");

        // Query for non-existent user
        System.Console.WriteLine("  Testing query for non-existent user...");
        var result2 = await _gam.ResearchAsync(new ResearchRequest
        {
            OwnerId = "non-existent-user-12345",
            Query = "anything"
        });
        System.Console.WriteLine($"    Non-existent user: {result2.Pages.Count} pages found");

        System.Console.WriteLine();
    }

    private async Task TestTokenBudgetAsync()
    {
        System.Console.WriteLine("TEST 4: Token budget limits");
        System.Console.WriteLine(new string('-', 40));

        // Test with small token budget
        System.Console.WriteLine("  Testing with small token budget (100)...");
        var smallBudget = await _gam.ResearchAsync(new ResearchRequest
        {
            OwnerId = _ownerId,
            Query = "programming concepts",
            Options = new ResearchOptions { MaxContextTokens = 100 }
        });
        System.Console.WriteLine($"    Small budget: {smallBudget.Pages.Count} pages, {smallBudget.TotalTokens} tokens");

        // Test with large token budget
        System.Console.WriteLine("  Testing with large token budget (10000)...");
        var largeBudget = await _gam.ResearchAsync(new ResearchRequest
        {
            OwnerId = _ownerId,
            Query = "programming concepts",
            Options = new ResearchOptions { MaxContextTokens = 10000 }
        });
        System.Console.WriteLine($"    Large budget: {largeBudget.Pages.Count} pages, {largeBudget.TotalTokens} tokens");

        System.Console.WriteLine();
    }

    private async Task TestCrossTopicResearchAsync()
    {
        System.Console.WriteLine("TEST 5: Cross-topic research");
        System.Console.WriteLine(new string('-', 40));

        // Query that should match multiple topics
        var queries = new[]
        {
            "What have we discussed about software development best practices?",
            "Summarize everything about infrastructure and deployment",
            "What do you know about my coding preferences?"
        };

        foreach (var query in queries)
        {
            System.Console.WriteLine($"  Query: \"{query}\"");
            var context = await _gam.ResearchAsync(new ResearchRequest
            {
                OwnerId = _ownerId,
                Query = query,
                Options = new ResearchOptions { MaxContextTokens = 2000 }
            });
            System.Console.WriteLine($"    Found: {context.Pages.Count} pages across topics");
            System.Console.WriteLine($"    Tokens: {context.TotalTokens}, Iterations: {context.IterationsPerformed}");
            
            if (context.Pages.Count > 0)
            {
                System.Console.WriteLine("    Topics covered:");
                foreach (var page in context.Pages.Take(3))
                {
                    var preview = page.Content.Split('\n').FirstOrDefault(l => l.Contains("User:"))?.Trim() ?? "N/A";
                    System.Console.WriteLine($"      - {preview[..Math.Min(60, preview.Length)]}...");
                }
            }
            System.Console.WriteLine();
        }
    }
}
