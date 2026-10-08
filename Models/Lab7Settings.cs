namespace DeFi.Models;

public sealed class Lab7Settings
{
    public string RouterAddress { get; set; } = string.Empty;

    public TokenSettings TokenA { get; set; } = new();

    public TokenSettings TokenB { get; set; } = new();

    public decimal PoolLiquidityA { get; set; } = 50_000m;

    public decimal PoolLiquidityB { get; set; } = 50_000m;

    public decimal DepositAmount { get; set; } = 1000m;

    public decimal RewardAmount { get; set; } = 100m;

    public decimal RewardThreshold { get; set; } = 10m;

    public int BotPollingIntervalSeconds { get; set; } = 12;

    public bool StopAfterFirstCompound { get; set; } = true;
}

public sealed class TokenSettings
{
    public string Name { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public decimal InitialSupply { get; set; } = 1_000_000m;
}