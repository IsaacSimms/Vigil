// == TokenUsage Value Object (from Diagram 3) == //

namespace Vigil.Domain.ValueObjects;

/// <summary>
/// Captured from the Grok response (via OpenAI SDK usage). 
/// Stored on AnalyzerProvenance so the system can observe its own spend.
/// Input = assembled bundle + prompt; Output = the structured diagnosis.
/// </summary>
public sealed record TokenUsage(int InputTokens, int OutputTokens);
