namespace DeFi.Models;

public sealed class Lab6Settings
{
    public StablecoinSettings Stablecoin { get; set; } = new();

    public string PriceFeedAddress { get; set; } = "0x694AA1769357215DE4FAC081bf1f309aDC325306";

    public decimal CollateralEth { get; set; } = 0.01m;

    public decimal TargetHealthFactor { get; set; } = 1.1m;

    public decimal CrashCollateralPercent { get; set; } = 20m;

    public string LiquidatorPrivateKey { get; set; } = string.Empty;

    public decimal LiquidatorMinEth { get; set; } = 0.02m;

    public int BotPollingIntervalSeconds { get; set; } = 12;

    public bool StopAfterFirstLiquidation { get; set; } = true;

    public List<string> MonitoredUsers { get; set; } = new();
}

public sealed class StablecoinSettings
{
    public string Name { get; set; } = "RubanUSD";
    public string Symbol { get; set; } = "RUBUSD";
}