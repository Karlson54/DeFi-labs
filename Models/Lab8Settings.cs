namespace DeFi.Models;

public sealed class Lab8Settings
{
    public SourceChainSettings Source { get; set; } = new();

    public DestinationChainSettings Destination { get; set; } = new();

    public string MessageText { get; set; } = "Привіт з Ethereum Sepolia!";

    public decimal LinkFundingAmount { get; set; } = 1m;

    public int TrackPollingIntervalSeconds { get; set; } = 15;

    public int TrackTimeoutMinutes { get; set; } = 40;

    public string ExplorerMessageUrl { get; set; } = "https://ccip.chain.link/msg/";
}

public sealed class SourceChainSettings
{
    public string RouterAddress { get; set; } = string.Empty;

    public string LinkTokenAddress { get; set; } = string.Empty;
}

public sealed class DestinationChainSettings
{
    public string Name { get; set; } = "Arbitrum Sepolia";

    public string RpcUrl { get; set; } = string.Empty;

    public long ChainId { get; set; } = 421614;

    public ulong ChainSelector { get; set; }

    public string RouterAddress { get; set; } = string.Empty;
}