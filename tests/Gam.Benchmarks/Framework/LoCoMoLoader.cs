using System.Text.Json;
using System.Text.RegularExpressions;

namespace Gam.Benchmarks.Framework;

/// <summary>
/// Loader for LoCoMo dataset JSON files.
/// Handles the complex nested structure with dynamic session keys.
/// </summary>
public static class LoCoMoLoader
{
    private static readonly Regex SessionPattern = new(@"^session_(\d+)$", RegexOptions.Compiled);
    private static readonly Regex DateTimePattern = new(@"^session_(\d+)_date_time$", RegexOptions.Compiled);
    private static readonly Regex SummaryPattern = new(@"^session_(\d+)_summary$", RegexOptions.Compiled);

    public static async Task<List<LoCoMoSample>> LoadAsync(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        
        var samples = new List<LoCoMoSample>();
        
        // Dataset can be a list or {"samples": [...]}
        JsonElement root = doc.RootElement;
        JsonElement samplesArray = root.ValueKind == JsonValueKind.Array 
            ? root 
            : root.GetProperty("samples");
        
        foreach (var sampleElement in samplesArray.EnumerateArray())
        {
            var sample = ParseSample(sampleElement);
            samples.Add(sample);
        }
        
        return samples;
    }
    
    private static LoCoMoSample ParseSample(JsonElement element)
    {
        var sample = new LoCoMoSample
        {
            SampleId = element.TryGetProperty("sample_id", out var sid) ? sid.GetString() : null,
            Conversation = new LoCoMoConversation(),
            Questions = []
        };
        
        // Parse conversation
        if (element.TryGetProperty("conversation", out var convElement))
        {
            sample.Conversation = ParseConversation(convElement);
        }
        
        // Parse QA
        if (element.TryGetProperty("qa", out var qaElement))
        {
            foreach (var qaItem in qaElement.EnumerateArray())
            {
                sample.Questions.Add(ParseQuestion(qaItem));
            }
        }
        
        return sample;
    }
    
    private static LoCoMoConversation ParseConversation(JsonElement element)
    {
        var conv = new LoCoMoConversation
        {
            SpeakerA = element.TryGetProperty("speaker_a", out var sa) ? sa.GetString() ?? "" : "",
            SpeakerB = element.TryGetProperty("speaker_b", out var sb) ? sb.GetString() ?? "" : ""
        };
        
        // Store extension data for session extraction
        conv.ExtensionData = new Dictionary<string, object>();
        
        foreach (var prop in element.EnumerateObject())
        {
            if (prop.Name is "speaker_a" or "speaker_b") continue;
            
            // Store as JsonElement for later parsing
            conv.ExtensionData[prop.Name] = prop.Value.Clone();
        }
        
        return conv;
    }
    
    private static LoCoMoQuestion ParseQuestion(JsonElement element)
    {
        var q = new LoCoMoQuestion
        {
            Question = element.TryGetProperty("question", out var qp) ? qp.GetString() ?? "" : "",
            Category = element.TryGetProperty("category", out var cat) ? cat.GetInt32() : 0,
            Evidence = [],
            AdversarialAnswer = element.TryGetProperty("adversarial_answer", out var aa) ? aa.GetString() : null
        };
        
        // Answer can be string or number
        if (element.TryGetProperty("answer", out var ans))
        {
            q.Answer = ans.ValueKind switch
            {
                JsonValueKind.String => ans.GetString(),
                JsonValueKind.Number => ans.GetInt32(),
                _ => ans.ToString()
            };
        }
        
        // Evidence
        if (element.TryGetProperty("evidence", out var ev) && ev.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in ev.EnumerateArray())
            {
                var evStr = e.GetString();
                if (!string.IsNullOrEmpty(evStr))
                    q.Evidence.Add(evStr);
            }
        }
        
        return q;
    }
    
    /// <summary>
    /// Extract sessions from a conversation's extension data.
    /// Sessions are stored as session_1, session_2, etc.
    /// </summary>
    public static List<LoCoMoSession> ExtractSessions(LoCoMoConversation conversation)
    {
        var sessions = new Dictionary<int, LoCoMoSession>();
        var data = conversation.ExtensionData ?? new Dictionary<string, object>();
        
        // First pass: collect all session data
        foreach (var (key, value) in data)
        {
            // Check for session_N (turns)
            var sessionMatch = SessionPattern.Match(key);
            if (sessionMatch.Success && value is JsonElement turnsElement && turnsElement.ValueKind == JsonValueKind.Array)
            {
                int idx = int.Parse(sessionMatch.Groups[1].Value);
                if (!sessions.ContainsKey(idx))
                    sessions[idx] = new LoCoMoSession { Index = idx };
                
                sessions[idx].Turns = ParseTurns(turnsElement);
                continue;
            }
            
            // Check for session_N_date_time
            var dtMatch = DateTimePattern.Match(key);
            if (dtMatch.Success && value is JsonElement dtElement && dtElement.ValueKind == JsonValueKind.String)
            {
                int idx = int.Parse(dtMatch.Groups[1].Value);
                if (!sessions.ContainsKey(idx))
                    sessions[idx] = new LoCoMoSession { Index = idx };
                
                sessions[idx].DateTime = dtElement.GetString() ?? "";
                continue;
            }
            
            // Check for session_N_summary
            var sumMatch = SummaryPattern.Match(key);
            if (sumMatch.Success && value is JsonElement sumElement && sumElement.ValueKind == JsonValueKind.String)
            {
                int idx = int.Parse(sumMatch.Groups[1].Value);
                if (!sessions.ContainsKey(idx))
                    sessions[idx] = new LoCoMoSession { Index = idx };
                
                sessions[idx].Summary = sumElement.GetString();
            }
        }
        
        return sessions.Values.OrderBy(s => s.Index).ToList();
    }
    
    private static List<LoCoMoTurn> ParseTurns(JsonElement element)
    {
        var turns = new List<LoCoMoTurn>();
        
        foreach (var turnElement in element.EnumerateArray())
        {
            var turn = new LoCoMoTurn
            {
                Speaker = turnElement.TryGetProperty("speaker", out var sp) ? sp.GetString() ?? "" : "",
                DialogId = turnElement.TryGetProperty("dia_id", out var did) ? did.GetString() ?? "" : "",
                Text = turnElement.TryGetProperty("text", out var txt) ? txt.GetString() ?? "" : "",
                ImageCaption = turnElement.TryGetProperty("blip_caption", out var cap) ? cap.GetString() : null
            };
            
            // Image URLs
            if (turnElement.TryGetProperty("img_url", out var urls) && urls.ValueKind == JsonValueKind.Array)
            {
                turn.ImageUrls = [];
                foreach (var url in urls.EnumerateArray())
                {
                    var urlStr = url.GetString();
                    if (!string.IsNullOrEmpty(urlStr))
                        turn.ImageUrls.Add(urlStr);
                }
            }
            
            turns.Add(turn);
        }
        
        return turns;
    }
    
    /// <summary>
    /// Format a session as text for memorization.
    /// Matches the format used in the original GAM Python implementation.
    /// </summary>
    public static string FormatSessionAsText(LoCoMoSession session, string speakerA, string speakerB)
    {
        var lines = new List<string>
        {
            $"=== SESSION {session.Index} - Dialogue Time: {session.DateTime} ===",
            ""
        };
        
        foreach (var turn in session.Turns)
        {
            lines.Add($"{turn.Speaker} ({turn.DialogId}): {turn.Text}");
            
            // Include image caption if present
            if (!string.IsNullOrEmpty(turn.ImageCaption))
            {
                lines.Add($"  [Image: {turn.ImageCaption}]");
            }
        }
        
        if (!string.IsNullOrEmpty(session.Summary))
        {
            lines.Add("");
            lines.Add($"Session {session.Index} summary: {session.Summary}");
        }
        
        return string.Join("\n", lines).Trim();
    }
}
