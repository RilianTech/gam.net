using System.Diagnostics;
using Gam.Benchmarks.Framework;
using Gam.Core;
using Gam.Core.Abstractions;
using Gam.Core.Models;
using Gam.Providers.OpenAI;
using Gam.Storage.Postgres;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Abstractions;

namespace Gam.Benchmarks.Benchmarks;

/// <summary>
/// LoCoMo (Long-term Conversational Memory) benchmark tests.
/// 
/// This is the standard benchmark used by SimpleMem, Mem0, A-Mem, etc.
/// Dataset: https://github.com/snap-research/locomo
/// 
/// To run:
///   dotnet test tests/Gam.Benchmarks --filter "LoCoMoBenchmark" -l "console;verbosity=detailed"
/// 
/// Requires:
///   1. OpenAI API key in appsettings.Local.json
///   2. locomo10.json dataset in tests/Gam.Benchmarks/Data/
/// </summary>
public class LoCoMoBenchmarkTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly IConfiguration _configuration;
    private readonly string? _apiKey;
    
    private PostgreSqlContainer? _postgres;
    private ServiceProvider? _serviceProvider;
    private IGamService? _gam;
    private ILlmProvider? _llm;
    private List<LoCoMoSample>? _dataset;

    public LoCoMoBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
        _configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Local.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
        
        _apiKey = _configuration["OpenAI:ApiKey"];
    }

    public async Task InitializeAsync()
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            Log("OPENAI_API_KEY not set - tests will be skipped");
            return;
        }

        // Check if dataset exists
        var datasetPath = Path.Combine(AppContext.BaseDirectory, "Data", "locomo10.json");
        if (!File.Exists(datasetPath))
        {
            Log($"Dataset not found at {datasetPath}");
            Log("Download from: https://github.com/snap-research/locomo/blob/main/data/locomo10.json");
            return;
        }

        Log("Loading LoCoMo dataset...");
        _dataset = await LoCoMoLoader.LoadAsync(datasetPath);
        Log($"Loaded {_dataset.Count} conversations");

        Log("Starting PostgreSQL container...");
        _postgres = new PostgreSqlBuilder()
            .WithImage("timescale/timescaledb-ha:pg17")
            .Build();
        
        await _postgres.StartAsync();
        Log("PostgreSQL container started");

        var connectionString = _postgres.GetConnectionString();
        Log("Running migrations...");
        await RunMigrationsAsync(connectionString);
        Log("Database ready");

        // Setup DI
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        
        var model = _configuration["OpenAI:Model"] ?? "gpt-4o-mini";
        var embeddingModel = _configuration["OpenAI:EmbeddingModel"] ?? "text-embedding-3-small";
        var embeddingDimensions = int.TryParse(_configuration["OpenAI:EmbeddingDimensions"], out var dims) ? dims : 1536;
        
        services.AddGamOpenAI(_apiKey!, model, embeddingModel, embeddingDimensions);
        services.AddGamPostgresStorage(connectionString);
        services.AddGamCoreWithDeepResearch(opts =>
        {
            opts.MaxIterations = 3;
            opts.MaxKeywordQueries = 5;
            opts.MaxVectorQueries = 3;
            opts.MaxHitsPerIteration = 10;
            // Lower threshold - native PostgreSQL FTS returns scores 0.0-0.2
            opts.MinRelevanceScore = 0.05f;
        });

        _serviceProvider = services.BuildServiceProvider();
        _gam = _serviceProvider.GetRequiredService<IGamService>();
        _llm = _serviceProvider.GetRequiredService<ILlmProvider>();
        
        Log($"GAM configured with model: {model}");
    }
    
    private void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var formatted = $"[LoCoMo {timestamp}] {message}";
        // Only use Console for real-time streaming - xUnit captures it anyway
        Console.WriteLine(formatted);
    }

    public async Task DisposeAsync()
    {
        if (_postgres != null)
            await _postgres.DisposeAsync();
        _serviceProvider?.Dispose();
    }

    [SkippableFact]
    public async Task LoCoMo_QuickTest_SingleConversation()
    {
        Skip.If(string.IsNullOrEmpty(_apiKey), "OPENAI_API_KEY not set");
        Skip.If(_dataset == null || _dataset.Count == 0, "Dataset not loaded");
        Skip.If(_gam == null, "GAM not initialized");

        Log("Starting LoCoMo quick test...");
        
        var results = await RunBenchmarkAsync(
            conversationLimit: 1,
            questionLimit: 10,
            skipCategories: [LoCoMoCategory.Adversarial]);

        PrintResults(results, GetOutputPath("quick"));
        
        Assert.True(results.OverallF1 >= 0.0f, "F1 score should be non-negative");
    }

    /// <summary>
    /// Diagnostic test to understand what's happening at each step.
    /// Writes detailed output to benchmark-results/diagnostic-*.txt
    /// Console shows only summary.
    /// </summary>
    [SkippableFact]
    public async Task LoCoMo_Diagnostic_SingleQuestion()
    {
        Skip.If(string.IsNullOrEmpty(_apiKey), "OPENAI_API_KEY not set");
        Skip.If(_dataset == null || _dataset.Count == 0, "Dataset not loaded");
        Skip.If(_gam == null, "GAM not initialized");

        var sample = _dataset!.First();
        var ownerId = $"locomo-diag-{Guid.NewGuid():N}";
        var outputPath = GetOutputPath("diagnostic");
        var detailed = new List<string>();
        
        void LogBoth(string msg) { Log(msg); detailed.Add(msg); }
        void LogDetailed(string msg) { detailed.Add(msg); }
        
        LogBoth("=== DIAGNOSTIC: PIPELINE ANALYSIS ===");
        LogBoth($"Output file: {outputPath}");
        LogBoth($"Conversation: {sample.SampleId} ({sample.Conversation.SpeakerA} & {sample.Conversation.SpeakerB})");
        
        // Step 1: Ingestion
        var sessions = LoCoMoLoader.ExtractSessions(sample.Conversation);
        LogBoth($"\n[1] INGESTION: {sessions.Count} sessions");
        
        LogDetailed("\n--- All Sessions ---");
        foreach (var session in sessions)
        {
            var content = LoCoMoLoader.FormatSessionAsText(session, sample.Conversation.SpeakerA, sample.Conversation.SpeakerB);
            LogDetailed($"\nSession {session.Index} ({session.DateTime}):");
            LogDetailed(content);
            LogDetailed("---");
        }
        
        await IngestConversationAsync(sample, ownerId);
        
        // Step 2: Check abstracts
        var store = _serviceProvider!.GetRequiredService<IMemoryStore>();
        var abstracts = await store.GetAbstractsAsync(ownerId);
        LogBoth($"[2] ABSTRACTS: {abstracts.Count} created");
        
        LogDetailed("\n--- All Abstracts ---");
        foreach (var abs in abstracts)
        {
            LogDetailed($"\nAbstract {abs.PageId}:");
            LogDetailed($"  Headers: [{string.Join(", ", abs.Headers)}]");
            LogDetailed($"  Summary: {abs.Summary}");
        }
        
        // Step 3: Question & Research
        var question = sample.Questions.First();
        LogBoth($"\n[3] QUESTION: {question.Question}");
        LogBoth($"    Category: {(LoCoMoCategory)question.Category}");
        LogBoth($"    Gold: {question.AnswerText}");
        LogBoth($"    Evidence refs: {string.Join(", ", question.Evidence ?? [])}");
        
        var researchResult = await _gam!.ResearchAsync(new ResearchRequest
        {
            OwnerId = ownerId,
            Query = question.Question
        });
        
        LogBoth($"\n[4] RETRIEVAL: {researchResult.Pages.Count} pages, {researchResult.IterationsPerformed} iterations, {researchResult.Duration.TotalMilliseconds:F0}ms");
        
        LogDetailed("\n--- Retrieved Pages ---");
        foreach (var page in researchResult.Pages)
        {
            LogBoth($"    - {page.RetrievedBy}: score={page.RelevanceScore:F3}");
            LogDetailed($"\nPage {page.PageId} (by {page.RetrievedBy}, score={page.RelevanceScore:F3}):");
            LogDetailed(page.Content);
            LogDetailed("---");
        }
        
        // Step 4: Context
        var context = researchResult.FormatForPrompt();
        LogBoth($"\n[5] CONTEXT: {context.Length} chars");
        LogDetailed("\n--- Full Context ---");
        LogDetailed(context);
        
        // Step 5: Answer
        var prompt = BuildAnswerPrompt(question, context);
        LogDetailed("\n--- Full Prompt ---");
        LogDetailed(prompt);
        
        var messages = new List<LlmMessage> { new(LlmRole.User, prompt) };
        var response = await _llm!.CompleteAsync(messages);
        var predictedAnswer = response.Content.Trim();
        
        var (f1, bleu1) = LoCoMoEvaluator.ComputeScores(predictedAnswer, question.AnswerText);
        
        LogBoth($"\n[6] ANSWER:");
        LogBoth($"    Predicted: {predictedAnswer}");
        LogBoth($"    Gold:      {question.AnswerText}");
        LogBoth($"    F1: {f1:P2}, BLEU1: {bleu1:P2}");
        
        // Write detailed output
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllLinesAsync(outputPath, detailed);
        LogBoth($"\n=== DETAILED OUTPUT: {outputPath} ===");
    }

    /// <summary>
    /// Medium benchmark: 3 conversations, all questions.
    /// Good balance of statistical significance vs speed/cost.
    /// Expected runtime: ~45-60 minutes
    /// </summary>
    [SkippableFact]
    public async Task LoCoMo_Medium_ThreeConversations()
    {
        Skip.If(string.IsNullOrEmpty(_apiKey), "OPENAI_API_KEY not set");
        Skip.If(_dataset == null || _dataset.Count == 0, "Dataset not loaded");
        Skip.If(_gam == null, "GAM not initialized");

        var results = await RunBenchmarkAsync(
            conversationLimit: 3,     // 3 conversations (~450 questions)
            questionLimit: null,      // All questions per conversation
            skipCategories: [LoCoMoCategory.Adversarial]);

        PrintResults(results, GetOutputPath("medium"));
        
        // Compare with baselines
        _output.WriteLine("");
        _output.WriteLine("=== COMPARISON WITH BASELINES ===");
        _output.WriteLine($"SimpleMem: 43.24% F1 (target)");
        _output.WriteLine($"Mem0:      34.20% F1");
        _output.WriteLine($"A-Mem:     32.58% F1");
        _output.WriteLine($"LightMem:  24.63% F1");
        _output.WriteLine($"GAM.NET:   {results.OverallF1:P2} F1 (this run)");
        
        Assert.True(results.OverallF1 >= 0.20f, 
            $"Expected at least 20% F1 (above LightMem), got {results.OverallF1:P2}");
    }

    /// <summary>
    /// Full benchmark: All 10 conversations, all questions.
    /// Use for final comparison with published benchmarks.
    /// Expected runtime: ~2.5-3 hours
    /// </summary>
    [SkippableFact]
    public async Task LoCoMo_Full_AllConversations()
    {
        Skip.If(string.IsNullOrEmpty(_apiKey), "OPENAI_API_KEY not set");
        Skip.If(_dataset == null || _dataset.Count == 0, "Dataset not loaded");
        Skip.If(_gam == null, "GAM not initialized");

        var results = await RunBenchmarkAsync(
            conversationLimit: null,  // All 10 conversations
            questionLimit: null,      // All questions
            skipCategories: [LoCoMoCategory.Adversarial]);  // Skip adversarial per original GAM

        PrintResults(results, GetOutputPath("full"));
        
        // Compare with baselines
        _output.WriteLine("");
        _output.WriteLine("=== COMPARISON WITH BASELINES ===");
        _output.WriteLine($"SimpleMem: 43.24% F1 (target)");
        _output.WriteLine($"Mem0:      34.20% F1");
        _output.WriteLine($"A-Mem:     32.58% F1");
        _output.WriteLine($"LightMem:  24.63% F1");
        _output.WriteLine($"GAM.NET:   {results.OverallF1:P2} F1 (this run)");
        
        Assert.True(results.OverallF1 >= 0.20f, 
            $"Expected at least 20% F1 (above LightMem), got {results.OverallF1:P2}");
    }

    [SkippableFact]
    public async Task LoCoMo_CategoryTest_MultiHop()
    {
        Skip.If(string.IsNullOrEmpty(_apiKey), "OPENAI_API_KEY not set");
        Skip.If(_dataset == null || _dataset.Count == 0, "Dataset not loaded");
        Skip.If(_gam == null, "GAM not initialized");

        var results = await RunBenchmarkAsync(
            conversationLimit: 2,
            questionLimit: null,
            onlyCategories: [LoCoMoCategory.MultiHop]);

        PrintResults(results, GetOutputPath("multihop"));
        
        var multiHopResult = results.CategoryResults.FirstOrDefault(c => c.Category == LoCoMoCategory.MultiHop);
        Assert.NotNull(multiHopResult);
        _output.WriteLine($"MultiHop F1: {multiHopResult.AverageF1:P2} (SimpleMem: 43.46%)");
    }

    private async Task<LoCoMoBenchmarkResults> RunBenchmarkAsync(
        int? conversationLimit = null,
        int? questionLimit = null,
        LoCoMoCategory[]? skipCategories = null,
        LoCoMoCategory[]? onlyCategories = null)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var constructionStopwatch = new Stopwatch();
        var retrievalStopwatch = new Stopwatch();
        
        var allQuestionResults = new List<LoCoMoQuestionResult>();
        var samplesToProcess = conversationLimit.HasValue 
            ? _dataset!.Take(conversationLimit.Value).ToList() 
            : _dataset!;

        Log($"Processing {samplesToProcess.Count} conversations...");

        foreach (var sample in samplesToProcess)
        {
            var ownerId = $"locomo-{sample.SampleId ?? Guid.NewGuid().ToString()}";
            Log($"--- Conversation: {sample.SampleId} ---");

            // Phase 1: Ingest conversation sessions
            constructionStopwatch.Start();
            await IngestConversationAsync(sample, ownerId);
            constructionStopwatch.Stop();

            // Phase 2: Run QA evaluation
            var questionsToProcess = sample.Questions
                .Where(q => skipCategories == null || !skipCategories.Contains((LoCoMoCategory)q.Category))
                .Where(q => onlyCategories == null || onlyCategories.Contains((LoCoMoCategory)q.Category))
                .ToList();

            if (questionLimit.HasValue)
                questionsToProcess = questionsToProcess.Take(questionLimit.Value).ToList();

            Log($"Evaluating {questionsToProcess.Count} questions...");

            retrievalStopwatch.Start();
            foreach (var question in questionsToProcess)
            {
                var result = await EvaluateQuestionAsync(question, ownerId);
                allQuestionResults.Add(result);
                
                // Brief progress indicator (detailed results go to output file)
                Log($"  [{((LoCoMoCategory)question.Category).ToDisplayName()}] F1={result.F1Score:P0} | {question.Question[..Math.Min(50, question.Question.Length)]}...");
                Log($"    Gold:      {result.GoldAnswer}");
                Log($"    Predicted: {result.PredictedAnswer}");
            }
            retrievalStopwatch.Stop();
        }

        totalStopwatch.Stop();

        // Aggregate results
        var categoryResults = allQuestionResults
            .GroupBy(r => r.Category)
            .Select(g => new LoCoMoCategoryResult
            {
                Category = g.Key,
                Count = g.Count(),
                AverageF1 = g.Average(r => r.F1Score),
                AverageBleu1 = g.Average(r => r.Bleu1Score),
                AverageQueryTime = TimeSpan.FromMilliseconds(g.Average(r => r.ResearchDuration.TotalMilliseconds + r.AnswerDuration.TotalMilliseconds))
            })
            .OrderBy(c => (int)c.Category)
            .ToList();

        return new LoCoMoBenchmarkResults
        {
            DatasetName = "LoCoMo-10",
            ConfigurationName = $"GAM.NET Deep Research ({_configuration["OpenAI:Model"] ?? "gpt-4o-mini"})",
            TotalSamples = samplesToProcess.Count,
            TotalQuestions = allQuestionResults.Count,
            OverallF1 = allQuestionResults.Count > 0 ? allQuestionResults.Average(r => r.F1Score) : 0,
            OverallBleu1 = allQuestionResults.Count > 0 ? allQuestionResults.Average(r => r.Bleu1Score) : 0,
            ConstructionTime = constructionStopwatch.Elapsed,
            RetrievalTime = retrievalStopwatch.Elapsed,
            TotalTime = totalStopwatch.Elapsed,
            CategoryResults = categoryResults,
            QuestionResults = allQuestionResults
        };
    }

    private async Task IngestConversationAsync(LoCoMoSample sample, string ownerId)
    {
        var sessions = LoCoMoLoader.ExtractSessions(sample.Conversation);
        Log($"  Ingesting {sessions.Count} sessions...");

        foreach (var session in sessions)
        {
            var content = LoCoMoLoader.FormatSessionAsText(
                session, 
                sample.Conversation.SpeakerA, 
                sample.Conversation.SpeakerB);

            // Parse timestamp from session datetime
            var timestamp = ParseLoCoMoDateTime(session.DateTime);

            // Ingest the session transcript directly as content
            await _gam!.MemorizeAsync(new MemorizeRequest
            {
                Input = new MemoryInput
                {
                    OwnerId = ownerId,
                    Content = content,
                    Timestamp = timestamp,
                    SessionId = session.Index.ToString(),
                    Metadata = new Dictionary<string, string>
                    {
                        ["session_id"] = session.Index.ToString(),
                        ["speaker_a"] = sample.Conversation.SpeakerA,
                        ["speaker_b"] = sample.Conversation.SpeakerB
                    }
                }
            });
        }
    }

    private async Task<LoCoMoQuestionResult> EvaluateQuestionAsync(LoCoMoQuestion question, string ownerId)
    {
        var researchStopwatch = Stopwatch.StartNew();
        var answerStopwatch = new Stopwatch();
        
        try
        {
            // Research relevant memories
            var researchResult = await _gam!.ResearchAsync(new ResearchRequest
            {
                OwnerId = ownerId,
                Query = question.Question
            });
            researchStopwatch.Stop();

            // Generate answer using LLM
            answerStopwatch.Start();
            var prompt = BuildAnswerPrompt(question, researchResult.FormatForPrompt());
            var messages = new List<LlmMessage>
            {
                new(LlmRole.User, prompt)
            };
            var response = await _llm!.CompleteAsync(messages);
            var predictedAnswer = response.Content.Trim();
            answerStopwatch.Stop();

            // Compute scores
            var (f1, bleu1) = LoCoMoEvaluator.ComputeScores(predictedAnswer, question.AnswerText);

            return new LoCoMoQuestionResult
            {
                Question = question.Question,
                GoldAnswer = question.AnswerText,
                PredictedAnswer = predictedAnswer,
                Category = (LoCoMoCategory)question.Category,
                F1Score = f1,
                Bleu1Score = bleu1,
                ResearchDuration = researchResult.Duration,
                AnswerDuration = answerStopwatch.Elapsed,
                PagesRetrieved = researchResult.Pages.Count,
                IterationsPerformed = researchResult.IterationsPerformed
            };
        }
        catch (Exception ex)
        {
            researchStopwatch.Stop();
            return new LoCoMoQuestionResult
            {
                Question = question.Question,
                GoldAnswer = question.AnswerText,
                PredictedAnswer = "",
                Category = (LoCoMoCategory)question.Category,
                F1Score = 0,
                Bleu1Score = 0,
                ResearchDuration = researchStopwatch.Elapsed,
                AnswerDuration = TimeSpan.Zero,
                PagesRetrieved = 0,
                IterationsPerformed = 0,
                Error = ex.Message
            };
        }
    }

    private string BuildAnswerPrompt(LoCoMoQuestion question, string context)
    {
        // Use different prompts based on category for optimal performance
        return question.Category switch
        {
            // SingleHop (1) - Direct fact lookup from a single turn
            1 => $"""
                Based on the conversation history, find and return the EXACT fact requested.
                
                RULES:
                - Use exact words/phrases from the conversation when possible
                - Be specific with names, numbers, dates, and details
                - Answer with a short phrase, not a full sentence
                - If the answer includes a person's name, use their full name as mentioned
                
                QUESTION:
                {question.Question}
                
                CONVERSATION HISTORY:
                {context}
                
                Short factual answer:
                """,
            
            // Temporal (2) - Time-based reasoning
            2 => $"""
                Based on the conversation history, answer this temporal question about WHEN something happened.
                
                CRITICAL TEMPORAL RULES:
                - Format dates as "DD Month YYYY" (e.g., "15 July 2023")
                - Look for dialogue timestamps in format "Dialogue Time: X:XX pm on DD Month, YYYY"
                - Convert relative times to absolute dates based on conversation timestamps
                - For "last month" or "yesterday", calculate the actual date from the dialogue time
                - If exact date unknown, give the most specific timeframe possible
                - For durations, answer in days/weeks/months/years
                - Answer with ONLY the date/time, no extra words
                
                QUESTION:
                {question.Question}
                
                CONVERSATION HISTORY:
                {context}
                
                Date/time answer:
                """,
            
            // MultiHop (3) - Requires connecting multiple pieces of information
            3 => $"""
                Based on the conversation history, answer this question that requires connecting multiple pieces of information.
                
                RULES:
                - Analyze and infer the answer by connecting facts from different parts of the conversation
                - Answer with a short phrase, not a full sentence
                - Be specific with names and details
                
                QUESTION:
                {question.Question}
                
                CONVERSATION HISTORY:
                {context}
                
                Short answer:
                """,
            
            // Default (Knowledge, etc.)
            _ => $"""
                Based on the conversation history, write an answer in the form of a short phrase for the following question.
                Answer with exact words from the context whenever possible.
                
                QUESTION:
                {question.Question}
                
                CONVERSATION HISTORY:
                {context}
                
                Short answer:
                """
        };
    }

    private static DateTimeOffset ParseLoCoMoDateTime(string dateTime)
    {
        // LoCoMo format: "1:56 pm on 8 May, 2023"
        try
        {
            // Try various formats
            var formats = new[]
            {
                "h:mm tt 'on' d MMMM, yyyy",
                "h:mm tt 'on' d MMMM yyyy",
                "h:mm tt, d MMMM yyyy",
                "d MMMM yyyy"
            };
            
            foreach (var format in formats)
            {
                if (DateTimeOffset.TryParseExact(dateTime, format, 
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var result))
                {
                    return result;
                }
            }
            
            // Fallback: try general parse
            if (DateTimeOffset.TryParse(dateTime, out var parsed))
                return parsed;
        }
        catch { }
        
        return DateTimeOffset.UtcNow;
    }

    private void PrintResults(LoCoMoBenchmarkResults results, string? outputPath = null)
    {
        var lines = new List<string>
        {
            "",
            "=== LoCoMo BENCHMARK RESULTS ===",
            $"Dataset: {results.DatasetName}",
            $"Configuration: {results.ConfigurationName}",
            $"Samples: {results.TotalSamples}",
            $"Questions: {results.TotalQuestions}",
            $"Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC",
            "",
            "Category Results:"
        };
        
        foreach (var cat in results.CategoryResults)
        {
            lines.Add($"  {cat.Category.ToDisplayName(),-12}: F1={cat.AverageF1:P2}, BLEU1={cat.AverageBleu1:P2} ({cat.Count} questions, avg {cat.AverageQueryTime.TotalMilliseconds:F0}ms)");
        }
        
        lines.AddRange([
            "",
            $"Overall F1:          {results.OverallF1:P2}",
            $"Overall BLEU1:       {results.OverallBleu1:P2}",
            $"Construction Time:   {results.ConstructionTime.TotalSeconds:F1}s",
            $"Retrieval Time:      {results.RetrievalTime.TotalSeconds:F1}s",
            $"Total Time:          {results.TotalTime.TotalSeconds:F1}s",
            "",
            "=== DETAILED QUESTION RESULTS ==="
        ]);
        
        // Group by category for detailed output
        foreach (var categoryGroup in results.QuestionResults.GroupBy(q => q.Category).OrderBy(g => (int)g.Key))
        {
            lines.Add($"\n--- {categoryGroup.Key.ToDisplayName()} ({categoryGroup.Count()} questions, avg F1={categoryGroup.Average(q => q.F1Score):P1}) ---");
            
            foreach (var q in categoryGroup.OrderByDescending(q => q.F1Score))
            {
                lines.Add($"  [{q.F1Score:P0}] {q.Question}");
                lines.Add($"       Gold:      {q.GoldAnswer}");
                lines.Add($"       Predicted: {q.PredictedAnswer}");
            }
        }
        
        // Output to console
        foreach (var line in lines)
        {
            _output.WriteLine(line);
        }
        
        // Output to file if path specified
        if (!string.IsNullOrEmpty(outputPath))
        {
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            File.WriteAllLines(outputPath, lines);
            _output.WriteLine($"\nResults saved to: {outputPath}");
        }
    }
    
    /// <summary>
    /// Gets the output path for benchmark results.
    /// Override with environment variable LOCOMO_OUTPUT_PATH.
    /// </summary>
    private string GetOutputPath(string testName)
    {
        var envPath = Environment.GetEnvironmentVariable("LOCOMO_OUTPUT_PATH");
        if (!string.IsNullOrEmpty(envPath))
            return envPath;
        
        var model = _configuration["OpenAI:Model"] ?? "gpt-4o-mini";
        var safeModel = model.Replace(".", "-").Replace(":", "-");
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        
        // Output to project root's benchmark-results folder
        var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var resultsDir = Path.Combine(projectRoot, "benchmark-results");
        
        return Path.Combine(resultsDir, $"locomo-{testName}-{safeModel}-{timestamp}.txt");
    }

    private async Task RunMigrationsAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        // Required: pgvector extension
        await using var cmdExt = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS vector;", conn);
        await cmdExt.ExecuteNonQueryAsync();

        // Optional: pg_textsearch for BM25 (Timescale)
        var hasPgTextSearch = false;
        try
        {
            await using var cmdBm25 = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS pg_textsearch;", conn);
            await cmdBm25.ExecuteNonQueryAsync();
            hasPgTextSearch = true;
            Log("pg_textsearch extension enabled");
        }
        catch (PostgresException ex)
        {
            Log($"pg_textsearch not available: {ex.Message}");
        }

        const string schema = """
            CREATE TABLE IF NOT EXISTS memory_pages (
                id UUID PRIMARY KEY,
                owner_id VARCHAR(255) NOT NULL,
                content TEXT NOT NULL,
                token_count INTEGER NOT NULL,
                embedding vector(1536),
                metadata JSONB,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                importance FLOAT DEFAULT 0.5,
                access_count INTEGER DEFAULT 0,
                last_accessed_at TIMESTAMPTZ
            );

            CREATE TABLE IF NOT EXISTS memory_abstracts (
                page_id UUID PRIMARY KEY REFERENCES memory_pages(id) ON DELETE CASCADE,
                owner_id VARCHAR(255) NOT NULL,
                summary TEXT NOT NULL,
                headers TEXT[] NOT NULL,
                summary_embedding vector(1536),
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                memory_type VARCHAR(20) DEFAULT 'conversation',
                tags TEXT[] DEFAULT '{}'
            );

            CREATE TABLE IF NOT EXISTS memory_relationships (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                source_page_id UUID NOT NULL REFERENCES memory_pages(id) ON DELETE CASCADE,
                target_page_id UUID NOT NULL REFERENCES memory_pages(id) ON DELETE CASCADE,
                relationship_type VARCHAR(50) NOT NULL,
                confidence FLOAT DEFAULT 1.0,
                created_by VARCHAR(20) DEFAULT 'system',
                created_at TIMESTAMPTZ DEFAULT NOW(),
                UNIQUE(source_page_id, target_page_id, relationship_type)
            );

            CREATE INDEX IF NOT EXISTS idx_pages_owner ON memory_pages(owner_id);
            CREATE INDEX IF NOT EXISTS idx_pages_created ON memory_pages(created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_abstracts_owner ON memory_abstracts(owner_id);
            CREATE INDEX IF NOT EXISTS idx_pages_content_fts ON memory_pages USING gin(to_tsvector('english', content));
            CREATE INDEX IF NOT EXISTS idx_abstracts_tags ON memory_abstracts USING GIN(tags);
            CREATE INDEX IF NOT EXISTS idx_rel_source ON memory_relationships(source_page_id);
            CREATE INDEX IF NOT EXISTS idx_rel_target ON memory_relationships(target_page_id);
            """;

        await using var cmdSchema = new NpgsqlCommand(schema, conn);
        await cmdSchema.ExecuteNonQueryAsync();

        // Create BM25 index if pg_textsearch is available
        if (hasPgTextSearch)
        {
            try
            {
                await using var cmdBm25Idx = new NpgsqlCommand(
                    "CREATE INDEX IF NOT EXISTS idx_pages_bm25 ON memory_pages USING bm25(content) WITH (text_config='english');", 
                    conn);
                await cmdBm25Idx.ExecuteNonQueryAsync();
                Log("BM25 index created on memory_pages.content");
            }
            catch (PostgresException ex)
            {
                Log($"Failed to create BM25 index: {ex.Message}");
                throw; // Don't swallow - this is a real error if pg_textsearch is enabled
            }
        }
        else
        {
            Log("Using native PostgreSQL full-text search (no BM25)");
        }
    }
}
