using System.Numerics;
using DeFi.Models;
using DeFi.Models.Contracts;
using Microsoft.Extensions.Options;
using Nethereum.Web3;

namespace DeFi.Services;

public interface IDexPoolService
{
    Task<PoolDeploymentResult> DeployAsync(string tokenA, string tokenB, CancellationToken cancellationToken = default);
    Task<PoolStateSnapshot> GetStateAsync(string poolAddress, CancellationToken cancellationToken = default);
    Task<string> AddLiquidityAsync(string poolAddress, decimal amountA, decimal amountB, CancellationToken cancellationToken = default);
    Task<SwapResult> SwapAForBAsync(string poolAddress, decimal amountIn, decimal slippageTolerancePercent, CancellationToken cancellationToken = default);
    Task<decimal> QuoteAsync(string poolAddress, decimal amountIn, decimal reserveIn, decimal reserveOut, CancellationToken cancellationToken = default);
    Task<int> GetFeeBpsAsync(string poolAddress, decimal amountIn, decimal reserveIn, CancellationToken cancellationToken = default);
}

public sealed class DexPoolService : IDexPoolService
{
    private const int Decimals = 18;

    private readonly IWeb3Factory _web3Factory;
    private readonly IContractArtifactProvider _artifacts;
    private readonly TimeSpan _timeout;

    public DexPoolService(IWeb3Factory web3Factory, IContractArtifactProvider artifacts, IOptions<Web3Settings> options)
    {
        _web3Factory = web3Factory;
        _artifacts = artifacts;
        _timeout = TimeSpan.FromSeconds(options.Value.TransactionTimeoutSeconds);
    }

    public async Task<PoolDeploymentResult> DeployAsync(string tokenA, string tokenB, CancellationToken cancellationToken = default)
    {
        var artifact = await _artifacts.GetAsync("DexPool", cancellationToken);

        var deployment = new DexPoolDeployment(artifact.Bytecode)
        {
            TokenA = tokenA,
            TokenB = tokenB
        };

        var receipt = await _web3Factory.Client.Eth
            .GetContractDeploymentHandler<DexPoolDeployment>()
            .SendRequestAndWaitForReceiptAsync(deployment)
            .WaitAsync(_timeout, cancellationToken);

        if (receipt.Status?.Value != 1)
        {
            throw new InvalidOperationException("Розгортання DexPool відхилено мережею (status = 0).");
        }

        return new PoolDeploymentResult(receipt.ContractAddress, tokenA, tokenB, WasAlreadyDeployed: false, receipt.TransactionHash);
    }

    public async Task<PoolStateSnapshot> GetStateAsync(string poolAddress, CancellationToken cancellationToken = default)
    {
        var reserveATask = QueryAsync(poolAddress, new ReserveAFunction(), cancellationToken);
        var reserveBTask = QueryAsync(poolAddress, new ReserveBFunction(), cancellationToken);

        await Task.WhenAll(reserveATask, reserveBTask);

        return new PoolStateSnapshot(
            Web3.Convert.FromWei(await reserveATask, Decimals),
            Web3.Convert.FromWei(await reserveBTask, Decimals));
    }

    public async Task<string> AddLiquidityAsync(string poolAddress, decimal amountA, decimal amountB, CancellationToken cancellationToken = default)
    {
        var function = new AddLiquidityFunction
        {
            AmountA = Web3.Convert.ToWei(amountA, Decimals),
            AmountB = Web3.Convert.ToWei(amountB, Decimals)
        };

        var receipt = await _web3Factory.Client.Eth
            .GetContractTransactionHandler<AddLiquidityFunction>()
            .SendRequestAndWaitForReceiptAsync(poolAddress, function)
            .WaitAsync(_timeout, cancellationToken);

        if (receipt.Status?.Value != 1)
        {
            throw new InvalidOperationException(
                "Транзакція addLiquidity відхилена. Найімовірніша причина — не виконано approve " +
                "на потрібну суму в контрактах токенів.");
        }

        return receipt.TransactionHash;
    }

    public async Task<SwapResult> SwapAForBAsync(string poolAddress, decimal amountIn, decimal slippageTolerancePercent, CancellationToken cancellationToken = default)
    {
        var stateBefore = await GetStateAsync(poolAddress, cancellationToken);

        var quoted = await QuoteAsync(poolAddress, amountIn, stateBefore.ReserveA, stateBefore.ReserveB, cancellationToken);
        var feeBps = await GetFeeBpsAsync(poolAddress, amountIn, stateBefore.ReserveA, cancellationToken);

        var minAmountOut = quoted * (1m - slippageTolerancePercent / 100m);

        var function = new SwapAForBFunction
        {
            AmountIn = Web3.Convert.ToWei(amountIn, Decimals),
            MinAmountOut = Web3.Convert.ToWei(minAmountOut, Decimals)
        };

        var receipt = await _web3Factory.Client.Eth
            .GetContractTransactionHandler<SwapAForBFunction>()
            .SendRequestAndWaitForReceiptAsync(poolAddress, function)
            .WaitAsync(_timeout, cancellationToken);

        if (receipt.Status?.Value != 1)
        {
            throw new InvalidOperationException(
                "Транзакція swapAForB відхилена. Перевірте approve на токен A та достатність резервів пулу.");
        }

        var stateAfter = await GetStateAsync(poolAddress, cancellationToken);

        var actualOut = stateBefore.ReserveB - stateAfter.ReserveB;

        return new SwapResult(
            amountIn,
            actualOut,
            quoted,
            minAmountOut,
            feeBps,
            stateBefore,
            stateAfter,
            receipt.TransactionHash,
            receipt.GasUsed?.Value ?? BigInteger.Zero);
    }

    public async Task<decimal> QuoteAsync(string poolAddress, decimal amountIn, decimal reserveIn, decimal reserveOut, CancellationToken cancellationToken = default)
    {
        var function = new GetAmountOutFunction
        {
            AmountIn = Web3.Convert.ToWei(amountIn, Decimals),
            ReserveIn = Web3.Convert.ToWei(reserveIn, Decimals),
            ReserveOut = Web3.Convert.ToWei(reserveOut, Decimals)
        };

        var raw = await QueryAsync(poolAddress, function, cancellationToken);
        return Web3.Convert.FromWei(raw, Decimals);
    }

    public async Task<int> GetFeeBpsAsync(string poolAddress, decimal amountIn, decimal reserveIn, CancellationToken cancellationToken = default)
    {
        var function = new GetFeeBpsFunction
        {
            AmountIn = Web3.Convert.ToWei(amountIn, Decimals),
            ReserveIn = Web3.Convert.ToWei(reserveIn, Decimals)
        };

        var raw = await QueryAsync(poolAddress, function, cancellationToken);
        return (int)raw;
    }

    private async Task<BigInteger> QueryAsync<TFunction>(string contractAddress, TFunction function, CancellationToken cancellationToken)
        where TFunction : Nethereum.Contracts.FunctionMessage, new()
    {
        return await _web3Factory.Client.Eth
            .GetContractQueryHandler<TFunction>()
            .QueryAsync<BigInteger>(contractAddress, function)
            .WaitAsync(_timeout, cancellationToken);
    }
}
