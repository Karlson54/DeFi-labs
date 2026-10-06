namespace DeFi.Models;

public sealed class Lab5Settings
{
    public StablecoinSettings Stablecoin { get; set; } = new();

    /// <summary>Початкова мок-ціна 1 ETH у USD (за умовою завдання — $2000).</summary>
    public decimal InitialEthUsdPrice { get; set; } = 2000m;

    /// <summary>Скільки ETH користувач вносить як заставу (за умовою — 2 ETH).</summary>
    public decimal CollateralEth { get; set; } = 2m;

    /// <summary>Скільки ETH користувач намагається зняти (за умовою — 1 ETH).</summary>
    public decimal WithdrawEth { get; set; } = 1m;
}

public sealed class StablecoinSettings
{
    public string Name { get; set; } = "RubanUSD";
    public string Symbol { get; set; } = "RUBUSD";
}