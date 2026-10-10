namespace DeFi.Models;

public sealed class Lab8Settings
{
    /// <summary>Вихідна мережа (L1): де розгортається CrossChainMessenger.</summary>
    public SourceChainSettings Source { get; set; } = new();

    /// <summary>Цільова мережа (L2): де розгортається контракт-отримувач.</summary>
    public DestinationChainSettings Destination { get; set; } = new();

    public string MessageText { get; set; } = "Привіт з Ethereum Sepolia!";

    /// <summary>Скільки LINK має бути на балансі контракту-месенджера для оплати комісії.</summary>
    public decimal LinkFundingAmount { get; set; } = 1m;

    public int TrackPollingIntervalSeconds { get; set; } = 15;

    /// <summary>Крос-чейн доставка в тестових мережах може тривати 10–30 хвилин.</summary>
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

    /// <summary>Звичайний EVM Chain ID — потрібен лише для підпису транзакцій у цільовій мережі.</summary>
    public long ChainId { get; set; } = 421614;

    /// <summary>CCIP Chain Selector — ідентифікатор мережі в протоколі CCIP (НЕ дорівнює Chain ID).</summary>
    public ulong ChainSelector { get; set; }

    public string RouterAddress { get; set; } = string.Empty;
}