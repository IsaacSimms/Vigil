// == GrokOptions (config for adapter per §7 and Phase 3 plan; resolved at composition root) == //

namespace Vigil.Infrastructure;

/// <summary>
/// Options for the Grok (xAI) adapter. Injected at composition root.
/// Never hard-coded; API key from env/XAI_API_KEY or secrets.
/// </summary>
public class GrokOptions
{
    public string Model { get; set; } = "grok-3"; // or grok-4.3 per design examples
    public int MaxTokens { get; set; } = 2000;
    public int TimeoutSeconds { get; set; } = 60;
    public double Temperature { get; set; } = 0.1; // low for determinism
    public string ApiKey { get; set; } = string.Empty; // set via config, not here
    public string BaseUrl { get; set; } = "https://api.x.ai/v1";
}
