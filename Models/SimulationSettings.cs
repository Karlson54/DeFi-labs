namespace DeFi.Models;

public sealed class SimulationSettings
{
    public TokenSettings TokenA { get; set; } = new();

    public TokenSettings TokenB { get; set; } = new();

    public decimal LiquidityAmountA { get; set; } = 1000m;

    public decimal LiquidityAmountB { get; set; } = 2000m;

    public decimal SwapAmountA { get; set; } = 50m;

    public decimal SlippageTolerancePercent { get; set; } = 1m;

    public string PartnerAddress { get; set; } = string.Empty;

    public decimal PartnerTransferAmount { get; set; } = 5000m;
}

public sealed class TokenSettings
{
    public string Name { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;

    public decimal InitialSupply { get; set; } = 1_000_000m;
}