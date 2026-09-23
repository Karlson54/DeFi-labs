namespace DeFi.Models;

public sealed class SwapRecordEntity
{
    public int Id { get; set; }
    public required string TransactionHash { get; set; }
    public int LogIndex { get; set; }
    public long BlockNumber { get; set; }
    public required string PoolAddress { get; set; }
    public required string Trader { get; set; }
    public required string TokenIn { get; set; }
    public decimal AmountIn { get; set; }
    public decimal AmountOut { get; set; }
    public int FeeBps { get; set; }
    public DateTimeOffset IndexedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class IndexerCheckpointEntity
{
    public long ChainId { get; set; }
    public required string PoolAddress { get; set; }
    public long LastProcessedBlock { get; set; }
}