using System.Numerics;
using Defi.Models;
using Defi.Models.Contracts;
using Microsoft.Extensions.Options;
using Nethereum.Web3;

namespace Defi.Services;

public interface IDefiIntegratorService
{
    Task<IntegratorDeploymentResult> DeployAsync(string routerAddress, CancellationToken cancellationToken = default);

    Task<LiquidityResult> ProvideLiquidityAsync(
        string integratorAddress, string tokenA, string tokenB,
        decimal amountA, decimal amountB, CancellationToken cancellationToken = default);

    Task<SwapResult> SwapTokensAsync(
        string integratorAddress, string tokenIn, string tokenOut,
        decimal amountIn, decimal amountOutMin, CancellationToken cancellationToken = default);
}

public sealed class DefiIntegratorService : IDefiIntegratorService
{
    private const int Decimals = 18;

    private readonly IWeb3Factory _web3Factory;
    private readonly IContractArtifactProvider _artifacts;
    private readonly TimeSpan _timeout;

    public DefiIntegratorService(IWeb3Factory web3Factory, IContractArtifactProvider artifacts, IOptions<Web3Settings> options)
    {
        _web3Factory = web3Factory;
        _artifacts = artifacts;
        _timeout = TimeSpan.FromSeconds(options.Value.TransactionTimeoutSeconds);
    }

    public async Task<IntegratorDeploymentResult> DeployAsync(string routerAddress, CancellationToken cancellationToken = default)
    {
        var artifact = await _artifacts.GetAsync("DefiIntegrator", cancellationToken);

        var deployment = new DefiIntegratorDeployment(artifact.Bytecode)
        {
            Router = routerAddress
        };

        var receipt = await _web3Factory.Client.Eth
            .GetContractDeploymentHandler<DefiIntegratorDeployment>()
            .SendRequestAndWaitForReceiptAsync(deployment)
            .WaitAsync(_timeout, cancellationToken);

        if (receipt.Status?.Value != 1)
        {
            throw new InvalidOperationException(
                "Розгортання DefiIntegrator відхилено мережею (status = 0). " +
                "Перевірте адресу Lab4Settings:RouterAddress та коректність байткоду артефакту.");
        }

        return new IntegratorDeploymentResult(receipt.ContractAddress, routerAddress, WasAlreadyDeployed: false, receipt.TransactionHash);
    }

    public async Task<LiquidityResult> ProvideLiquidityAsync(
        string integratorAddress, string tokenA, string tokenB,
        decimal amountA, decimal amountB, CancellationToken cancellationToken = default)
    {
        var function = new ProvideLiquidityFunction
        {
            TokenA = tokenA,
            TokenB = tokenB,
            AmountADesired = Web3.Convert.ToWei(amountA, Decimals),
            AmountBDesired = Web3.Convert.ToWei(amountB, Decimals)
        };

        var receipt = await _web3Factory.Client.Eth
            .GetContractTransactionHandler<ProvideLiquidityFunction>()
            .SendRequestAndWaitForReceiptAsync(integratorAddress, function)
            .WaitAsync(_timeout, cancellationToken);

        if (receipt.Status?.Value != 1)
        {
            throw new InvalidOperationException(
                "Транзакція provideLiquidity відхилена мережею. Найімовірніша причина — " +
                "не виконано approve на потрібну суму в контрактах токенів для адреси інтегратора, " +
                "або Router-адреса в Lab4Settings вказує на неіснуючий/несумісний контракт.");
        }

        return new LiquidityResult(amountA, amountB, receipt.TransactionHash, receipt.GasUsed?.Value ?? BigInteger.Zero);
    }

    public async Task<SwapResult> SwapTokensAsync(
        string integratorAddress, string tokenIn, string tokenOut,
        decimal amountIn, decimal amountOutMin, CancellationToken cancellationToken = default)
    {
        var function = new SwapTokensFunction
        {
            TokenIn = tokenIn,
            TokenOut = tokenOut,
            AmountIn = Web3.Convert.ToWei(amountIn, Decimals),
            AmountOutMin = Web3.Convert.ToWei(amountOutMin, Decimals)
        };

        var receipt = await _web3Factory.Client.Eth
            .GetContractTransactionHandler<SwapTokensFunction>()
            .SendRequestAndWaitForReceiptAsync(integratorAddress, function)
            .WaitAsync(_timeout, cancellationToken);

        if (receipt.Status?.Value != 1)
        {
            throw new InvalidOperationException(
                "Транзакція swapTokens відхилена мережею. Перевірте approve на tokenIn " +
                "для адреси інтегратора та наявність достатньої ліквідності у пулі Router-протоколу.");
        }

        return new SwapResult(amountIn, amountOutMin, receipt.TransactionHash, receipt.GasUsed?.Value ?? BigInteger.Zero);
    }
}