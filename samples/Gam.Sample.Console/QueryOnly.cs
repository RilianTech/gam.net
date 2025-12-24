using Gam.Core.Abstractions;
using Gam.Core.Models;

namespace Gam.Sample.Console;

public class QueryOnly
{
    private readonly IGamService _gam;

    public QueryOnly(IGamService gam)
    {
        _gam = gam;
    }

    public async Task RunAsync()
    {
        System.Console.WriteLine("GAM.NET Query-Only Sample");
        System.Console.WriteLine("=========================");
        System.Console.WriteLine();

        // List available owners
        System.Console.WriteLine("Enter owner ID to query (or press Enter for 'demo-user'):");
        var ownerId = System.Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(ownerId))
            ownerId = "demo-user";

        System.Console.WriteLine($"\nQuerying memories for: {ownerId}");
        System.Console.WriteLine(new string('-', 40));

        while (true)
        {
            System.Console.WriteLine("\nEnter your query (or 'quit' to exit):");
            var query = System.Console.ReadLine()?.Trim();
            
            if (string.IsNullOrEmpty(query) || query.Equals("quit", StringComparison.OrdinalIgnoreCase))
                break;

            System.Console.WriteLine("\nSearching...");
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var context = await _gam.ResearchAsync(new ResearchRequest
            {
                OwnerId = ownerId,
                Query = query
            });

            sw.Stop();

            System.Console.WriteLine($"\nResults: {context.Pages.Count} pages, {context.TotalTokens} tokens");
            System.Console.WriteLine($"Time: {sw.ElapsedMilliseconds}ms | Iterations: {context.IterationsPerformed}");
            System.Console.WriteLine();

            if (context.Pages.Count == 0)
            {
                System.Console.WriteLine("No relevant memories found.");
            }
            else
            {
                System.Console.WriteLine("=== Memory Context ===\n");
                System.Console.WriteLine(context.FormatForPrompt());
            }
        }

        System.Console.WriteLine("\nGoodbye!");
    }
}
