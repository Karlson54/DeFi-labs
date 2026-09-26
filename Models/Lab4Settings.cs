namespace Defi.Models;

public sealed class Lab4Settings
{
    public string RouterAddress { get; set; } = string.Empty;

    public TokenSettings TokenA { get; set; } = new();

    public TokenSettings TokenB { get; set; } = new();

    public decimal LiquidityAmountA { get; set; } = 500m;

    public decimal LiquidityAmountB { get; set; } = 1000m;

    public decimal SwapAmountIn { get; set; } = 25m;

    public decimal SwapAmountOutMin { get; set; } = 0m;
}

public sealed class TokenSettings
{
    public string Name { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public decimal InitialSupply { get; set; } = 1_000_000m;
}