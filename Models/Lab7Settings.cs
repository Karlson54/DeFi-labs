namespace DeFi.Models;

public sealed class Lab7Settings
{
    /// <summary>Адреса Router-контракту Uniswap V2 (або сумісного форку) — та сама, що в лабораторній №4.</summary>
    public string RouterAddress { get; set; } = string.Empty;

    /// <summary>Токен A — базовий актив сховища.</summary>
    public TokenSettings TokenA { get; set; } = new();

    /// <summary>Токен B — токен винагороди (його сховище продає у compound).</summary>
    public TokenSettings TokenB { get; set; } = new();

    /// <summary>Початкова ліквідність пари A/B у зовнішньому DEX (без неї обмін винагороди неможливий).</summary>
    public decimal PoolLiquidityA { get; set; } = 50_000m;

    public decimal PoolLiquidityB { get; set; } = 50_000m;

    /// <summary>Сума депозиту інвестора (за умовою завдання — 1000).</summary>
    public decimal DepositAmount { get; set; } = 1000m;

    /// <summary>Скільки Токена B "нараховує" фарм-протокол (надсилається прямим переказом на сховище).</summary>
    public decimal RewardAmount { get; set; } = 100m;

    /// <summary>Поріг винагороди, після якого бот-кіпер вважає виклик compound() вигідним.</summary>
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