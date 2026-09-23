namespace DeFi.Models;

public sealed class IndexerSettings
{
    public int PollingIntervalSeconds { get; set; } = 5;

    public long StartBlock { get; set; } = 0;

    public long MaxBlockRangePerPoll { get; set; } = 5000;
}

public sealed class CorsSettings
{
    public string AllowedOrigin { get; set; } = "http://localhost:5173";
}