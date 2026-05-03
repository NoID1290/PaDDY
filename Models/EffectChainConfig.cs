namespace PaDDY.Models;

/// <summary>
/// Serializable configuration for a single audio effect.
/// </summary>
public class EffectConfig
{
    public string EffectType { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = false;
    public Dictionary<string, double> Parameters { get; set; } = new();
}

/// <summary>
/// Serializable configuration for an ordered effect chain.
/// </summary>
public class EffectChainConfig
{
    public List<EffectConfig> Effects { get; set; } = new();
}
