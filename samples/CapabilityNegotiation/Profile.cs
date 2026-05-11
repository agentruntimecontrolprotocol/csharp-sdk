// Marketplace profile + per-tenant usage rollup.
namespace ARCP.Samples.CapabilityNegotiation;

public sealed record Profile(double CostPerMtok, int P50LatencyMs, string ModelClass);

public sealed class Usage
{
    public long TokensIn { get; set; }

    public long TokensOut { get; set; }

    public double CostUsd { get; set; }

    public Dictionary<string, double> ByPeer { get; } = new();
}
