// == GrokDiagnosisAnalyzer (Adapter (UL) for IDiagnosisAnalyzer using xAI/Grok via OpenAI-compatible SDK per §7 + Diagram 4) == //

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using Vigil.Domain.Abstractions;
using Vigil.Domain.Entities;
using Vigil.Domain.Enums;
using Vigil.Domain.Models;
using Vigil.Domain.ValueObjects;

namespace Vigil.Infrastructure;

/// <summary>
/// The primary Implementation (UL) of IDiagnosisAnalyzer using Grok (xAI) via the official OpenAI NuGet SDK pointed at https://api.x.ai/v1 with XAI_API_KEY.
/// All OpenAI SDK types (OpenAIClient, ChatClient, ChatMessage, ChatTool, ChatCompletion, etc.) are confined here.
/// Builds text-only content for the current phase.
/// Uses tool-use for the pinned report_diagnosis schema (max 5 causes, UUID citations, bounded confidence).
/// On failure (no tool call, timeout, etc.), returns typed AnalyzerResult with FallbackReason so UseCase can fallback to heuristic.
/// </summary>
public class GrokDiagnosisAnalyzer : IDiagnosisAnalyzer
{
    private readonly GrokOptions _options;

    public GrokDiagnosisAnalyzer(GrokOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<AnalyzerResult> AnalyzeAsync(EvidenceBundle bundle, string? symptom)
    {
        if (bundle == null || bundle.Artifacts.Count == 0)
        {
            return new AnalyzerResult(false, null, AnalyzerTier.Model, FallbackReason.OfflineFlag);
        }

        try
        {
            var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(_options.BaseUrl) };
            var credential = new ApiKeyCredential(_options.ApiKey);
            var client = new OpenAIClient(credential, clientOptions);
            var chatClient = client.GetChatClient(_options.Model);

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage("You are an expert systems root-cause analyst. Analyze ONLY the provided evidence artifacts. Use the report_diagnosis tool to produce a structured diagnosis. Every cause must cite one or more artifact IDs from the evidence (use the exact Guids as evidence_artifact_id). Limit to at most 5 causes. Be precise and cite evidence. If you cannot produce a valid diagnosis, refuse."),
                new UserChatMessage(BuildEvidenceText(bundle, symptom))
            };

            var tool = ChatTool.CreateFunctionTool(
                functionName: "report_diagnosis",
                functionDescription: "Report a structured diagnosis with ranked causes, each with description, optional causal chain, confidence (0-1), severity, category, and citations to evidence artifact IDs.",
                functionParameters: BinaryData.FromString(GetReportDiagnosisSchema())
            );

            var completionOptions = new ChatCompletionOptions
            {
                Tools = { tool },
                ToolChoice = ChatToolChoice.CreateFunctionChoice("report_diagnosis"),
                Temperature = (float)_options.Temperature,
                MaxOutputTokenCount = _options.MaxTokens
            };

            var completion = await chatClient.CompleteChatAsync(messages, completionOptions);

            if (completion.Value.ToolCalls.Count > 0)
            {
                var toolCall = completion.Value.ToolCalls[0] as ChatToolCall;
                if (toolCall != null && toolCall.FunctionName == "report_diagnosis")
                {
                    var raw = MapToolCallToRawDiagnosis(toolCall.FunctionArguments);
                    var usage = new TokenUsage(
                        completion.Value.Usage.InputTokenCount,
                        completion.Value.Usage.OutputTokenCount
                    );
                    return new AnalyzerResult(true, raw, AnalyzerTier.Model, null, usage);
                }
            }

            // No valid tool call -> refusal
            return new AnalyzerResult(false, null, AnalyzerTier.Model, FallbackReason.Refusal);
        }
        catch (Exception)
        {
            // Any SDK error -> api unavailable for fallback
            return new AnalyzerResult(false, null, AnalyzerTier.Model, FallbackReason.ApiUnavailable);
        }
    }

    private string BuildEvidenceText(EvidenceBundle bundle, string? symptom)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(symptom))
        {
            sb.AppendLine($"Symptom reported: {symptom}");
            sb.AppendLine();
        }
        sb.AppendLine("Evidence artifacts (use the Guid IDs for citations):");
        foreach (var artifact in bundle.Artifacts)
        {
            sb.AppendLine($"ID: {artifact.Id}");
            sb.AppendLine($"Kind: {artifact.Kind}");
            sb.AppendLine($"Timestamp: {artifact.Timestamp}");
            sb.AppendLine($"Resource: {artifact.Resource?.Kind}:{artifact.Resource?.Identifier}");
            if (!string.IsNullOrWhiteSpace(artifact.TextContent))
            {
                sb.AppendLine("Content:");
                sb.AppendLine(artifact.TextContent);
            }
            else if (artifact.ImageBytes != null && artifact.ImageBytes.Length > 0)
            {
                sb.AppendLine("[IMAGE DATA - base64 would be sent in multimodal, but text-only phase]");
            }
            sb.AppendLine("---");
        }
        return sb.ToString();
    }

    private string GetReportDiagnosisSchema()
    {
        // Pinned schema per design (maxItems:5, UUID citations, bounded confidence, maps to Diagnosis shape)
        return @"{
  ""type"": ""object"",
  ""properties"": {
    ""summary"": {
      ""type"": ""string"",
      ""description"": ""Concise root cause summary."",
      ""maxLength"": 500
    },
    ""causes"": {
      ""type"": ""array"",
      ""maxItems"": 5,
      ""items"": {
        ""type"": ""object"",
        ""properties"": {
          ""description"": {
            ""type"": ""string"",
            ""description"": ""What broke or the direct cause.""
          },
          ""causalChain"": {
            ""type"": ""string"",
            ""description"": ""Optional chain of events leading to the failure.""
          },
          ""confidence"": {
            ""type"": ""number"",
            ""minimum"": 0,
            ""maximum"": 1,
            ""description"": ""Confidence in this cause (0.0-1.0).""
          },
          ""severity"": {
            ""type"": ""string"",
            ""enum"": [""Low"", ""Medium"", ""High"", ""Critical""],
            ""description"": ""Impact severity.""
          },
          ""category"": {
            ""type"": ""string"",
            ""enum"": [""ConfigChange"", ""Deployment"", ""ResourceExhaustion"", ""Permission"", ""DependencyFailure"", ""External"", ""Other""],
            ""description"": ""Broad category of the cause.""
          },
          ""citations"": {
            ""type"": ""array"",
            ""items"": {
              ""type"": ""object"",
              ""properties"": {
                ""evidence_artifact_id"": {
                  ""type"": ""string"",
                  ""description"": ""Exact Guid of the EvidenceArtifact this supports (must match one from input).""
                },
                ""snippet"": {
                  ""type"": ""string"",
                  ""description"": ""Optional short quote or reference from the artifact for humans.""
                }
              },
              ""required"": [""evidence_artifact_id""]
            },
            ""description"": ""Citations to supporting evidence artifacts. Must be real IDs from the input.""
          }
        },
        ""required"": [""description"", ""confidence"", ""severity"", ""category"", ""citations""]
      }
    }
  },
  ""required"": [""summary"", ""causes""]
}";
    }

    private RawDiagnosis MapToolCallToRawDiagnosis(BinaryData functionArguments)
    {
        using var doc = JsonDocument.Parse(functionArguments);
        var root = doc.RootElement;

        var summary = root.GetProperty("summary").GetString() ?? "No summary provided";

        var causes = new List<CandidateCause>();
        if (root.TryGetProperty("causes", out var causesArray))
        {
            foreach (var causeElem in causesArray.EnumerateArray())
            {
                var description = causeElem.GetProperty("description").GetString() ?? "";
                var causalChain = causeElem.TryGetProperty("causalChain", out var cc) ? cc.GetString() : null;
                var confidence = causeElem.GetProperty("confidence").GetDouble();
                var severityStr = causeElem.GetProperty("severity").GetString();
                var categoryStr = causeElem.GetProperty("category").GetString();

                var citations = new List<Citation>();
                if (causeElem.TryGetProperty("citations", out var cits))
                {
                    foreach (var citElem in cits.EnumerateArray())
                    {
                        var idStr = citElem.GetProperty("evidence_artifact_id").GetString();
                        if (Guid.TryParse(idStr, out var evid))
                        {
                            var snippet = citElem.TryGetProperty("snippet", out var sn) ? sn.GetString() : null;
                            citations.Add(new Citation(evid, snippet));
                        }
                    }
                }

                var severity = Enum.TryParse<Severity>(severityStr, out var s) ? s : Severity.Medium;
                var category = Enum.TryParse<CauseCategory>(categoryStr, out var c) ? c : CauseCategory.Other;

                causes.Add(new CandidateCause(
                    description,
                    causalChain,
                    new Confidence(confidence),
                    severity,
                    category,
                    citations
                ));
            }
        }

        return new RawDiagnosis(summary, causes);
    }
}
