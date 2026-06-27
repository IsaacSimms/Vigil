// == GrokGrillAdvisor (primary Implementation of IGrillAdvisor using Grok for free-form NL in TUI sessions) == //
// Mirrors the Adapter pattern of GrokDiagnosisAnalyzer but without forced tool use / diagnosis schema.
// Plain chat completions so the model can converse naturally as a debugging partner.
// Receives compact context from SessionState (evidence count, summaries, token usage, last diagnosis, cwd)
// so responses can reference prior turns and loaded artifacts without leaking full bundles.
// All OpenAI SDK types confined here. Falls back gracefully if no key.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using Vigil.Domain.Abstractions;

namespace Vigil.Infrastructure;

public class GrokGrillAdvisor : IGrillAdvisor
{
    private readonly GrokOptions _options;

    public GrokGrillAdvisor(GrokOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<string> ConsultAsync(string message, string? cwd = null, Guid? lastDiagnosisId = null, string? compactContext = null)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            var ctx = string.IsNullOrWhiteSpace(compactContext) ? "" : $" Context: {compactContext}";
            var cwdNote = string.IsNullOrWhiteSpace(cwd) ? "" : $" (cwd: {cwd})";
            return $"[no XAI_API_KEY - heuristic mode] Understood: \"{message}\"{cwdNote}{ctx}. Use /diagnose for a full validated Diagnosis with citations and provenance.";
        }

        try
        {
            var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(_options.BaseUrl) };
            var credential = new ApiKeyCredential(_options.ApiKey);
            var client = new OpenAIClient(credential, clientOptions);
            var chatClient = client.GetChatClient(_options.Model);

            var sysPrompt = "You are an expert, precise systems debugging partner in an interactive Vigil terminal session. " +
                            "The engineer is investigating a live incident in a specific working directory. " +
                            "You have access to a compact session context (bounded evidence excerpts per loaded file, token usage so far, previous turns, last formal diagnosis id if any). " +
                            "Reference specific details from the context when relevant. Be concise but helpful. " +
                            "Ask clarifying questions if the evidence is ambiguous. " +
                            "If the user describes analysis of loaded or mentioned files (e.g. 'analyze these files in the folder', 'what is the issue and potential fix') or says 'use /diagnose', the TUI will automatically trigger the formal governed Diagnosis pipeline with citations, validation, and provenance (no need for explicit slash in many cases). " +
                            "For pure chat or when continuing investigation without the formal output, just converse. " +
                            "Never invent evidence that is not in the provided context.";

            var userMsg = new StringBuilder();
            userMsg.AppendLine($"Current query: {message}");
            if (!string.IsNullOrWhiteSpace(cwd))
                userMsg.AppendLine($"Working directory: {cwd}");
            if (!string.IsNullOrWhiteSpace(compactContext))
                userMsg.AppendLine($"Session context (evidence, prior turns, tokens, last diagnosis): {compactContext}");
            if (lastDiagnosisId.HasValue)
                userMsg.AppendLine($"Last formal diagnosis id: {lastDiagnosisId}");

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(sysPrompt),
                new UserChatMessage(userMsg.ToString())
            };

            var completionOptions = new ChatCompletionOptions
            {
                Temperature = (float)_options.Temperature,
                MaxOutputTokenCount = _options.MaxTokens
            };

            var completion = await chatClient.CompleteChatAsync(messages, completionOptions);
            var text = completion.Value.Content.Count > 0 ? completion.Value.Content[0].Text : "(no response text)";
            return text ?? "(empty response)";
        }
        catch (Exception ex)
        {
            return $"[Grok chat error during advisor consult: {ex.Message}] Understood the point about \"{message}\". Try /diagnose for the governed path.";
        }
    }
}
