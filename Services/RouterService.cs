using System.Numerics;
using DeFi.Models;
using DeFi.Models.Contracts;
using Microsoft.Extensions.Options;
using Nethereum.Web3;

namespace DeFi.Services;

public interface IRouterService
{
    Task<LiquidityResult> AddLiquidityAsync(
        string tokenA, string tokenB, decimal amountA, decimal amountB,
        string recipient, CancellationToken cancellationToken = default);
}

/// <summary>
/// Пряма взаємодія з Uniswap V2 Router (композитність з лабораторної №4).
/// Тут Router викликає сам клієнт (EOA) — щоб створити пул A/B, у який потім ходитиме сховище.
/// </summary>
public sealed class RouterService : IRouterService
{
    private const int Decimals = 18;

    private readonly IWeb3Factory _web3Factory;
    private readonly Lab7Settings _settings;
    private readonly TimeSpan _timeout;

    public RouterService(IWeb3Factory web3Factory, IOptions<Web3Settings> web3Options, IOptions<Lab7Settings> labOptions)
    {
        _web3Factory = web3Factory;
        _settings = labOptions.Value;
        _timeout = TimeSpan.FromSeconds(web3Options.Value.TransactionTimeoutSeconds);
    }

    public async Task<LiquidityResult> AddLiquidityAsync(
        string tokenA, string tokenB, decimal amountA, decimal amountB,
        string recipient, CancellationToken cancellationToken = default)
    {
        var function = new AddLiquidityFunction
        {
            TokenA = tokenA,
            TokenB = tokenB,
            AmountADesired = Web3.Convert.ToWei(amountA, Decimals),
            AmountBDesired = Web3.Convert.ToWei(amountB, Decimals),
            // Мінімуми = 1 — навчальне спрощення (як у DefiIntegrator із лабораторної №4).
            AmountAMin = 1,
            AmountBMin = 1,
            To = recipient, // LP-токени отримує інвестор, а не сховище
            Deadline = new BigInteger(DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds())
        };

        var receipt = await _web3Factory.Client.Eth
            .GetContractTransactionHandler<AddLiquidityFunction>()
            .SendRequestAndWaitForReceiptAsync(_settings.RouterAddress, function)
            .WaitAsync(_timeout, cancellationToken);

        if (receipt.Status?.Value != 1)
        {
            throw new InvalidOperationException(
                "Транзакція addLiquidity відхилена Router-ом. Перевірте Lab7Settings:RouterAddress " +
                "(має вказувати на Uniswap V2 Router02 у поточній мережі) та approve обох токенів для Router-а.");
        }

        return new LiquidityResult(amountA, amountB, receipt.TransactionHash, receipt.GasUsed?.Value ?? BigInteger.Zero);
    }
}