using System.Numerics;
using DeFi.Models;
using DeFi.Models.Contracts;
using Microsoft.Extensions.Options;
using Nethereum.Contracts;
using Nethereum.Web3;

namespace DeFi.Services;

public interface IStableEngineService
{
    Task<EngineDeploymentResult> DeployAsync(string stablecoinAddress, string priceFeedAddress, CancellationToken cancellationToken = default);

    Task<decimal> GetEthUsdPriceAsync(string engineAddress, CancellationToken cancellationToken = default);

    Task<DepositResult> DepositCollateralAsync(string engineAddress, decimal amountEth, CancellationToken cancellationToken = default);

    Task<PositionSnapshot> GetPositionAsync(string engineAddress, string user, CancellationToken cancellationToken = default);

    Task<MintResult> MintToHealthFactorAsync(string engineAddress, string user, decimal targetHealthFactor, CancellationToken cancellationToken = default);

    Task<InsolvencyResult> SimulateInsolvencyAsync(string engineAddress, string user, decimal percent, CancellationToken cancellationToken = default);
}

public sealed class StableEngineService : IStableEngineService
{
    private const int Decimals = 18;

    private const int RatioDenominator = 100;
    private const int BasisPoints = 10_000;
    private static readonly BigInteger Precision = BigInteger.Pow(10, Decimals);

    private static readonly BigInteger InfinityThreshold = BigInteger.Pow(10, 30);

    private readonly IWeb3Factory _web3Factory;
    private readonly IContractArtifactProvider _artifacts;
    private readonly TimeSpan _timeout;

    public StableEngineService(IWeb3Factory web3Factory, IContractArtifactProvider artifacts, IOptions<Web3Settings> options)
    {
        _web3Factory = web3Factory;
        _artifacts = artifacts;
        _timeout = TimeSpan.FromSeconds(options.Value.TransactionTimeoutSeconds);
    }

    public async Task<EngineDeploymentResult> DeployAsync(string stablecoinAddress, string priceFeedAddress, CancellationToken cancellationToken = default)
    {
        var artifact = await _artifacts.GetAsync("StableEngine", cancellationToken);

        var deployment = new StableEngineDeployment(artifact.Bytecode)
        {
            Stablecoin = stablecoinAddress,
            PriceFeed = priceFeedAddress
        };

        var receipt = await _web3Factory.Client.Eth
            .GetContractDeploymentHandler<StableEngineDeployment>()
            .SendRequestAndWaitForReceiptAsync(deployment)
            .WaitAsync(_timeout, cancellationToken);

        if (receipt.Status?.Value != 1)
        {
            throw new InvalidOperationException(
                "Розгортання StableEngine відхилено мережею (status = 0). " +
                "Перевірте адреси стейблкоїна й оракула та те, що в артефакті лежить байткод " +
                "саме Lab6-версії контракту (конструктор приймає stablecoin_ та priceFeed_).");
        }

        return new EngineDeploymentResult(
            receipt.ContractAddress, stablecoinAddress, priceFeedAddress,
            WasAlreadyDeployed: false, receipt.TransactionHash);
    }

    public async Task<decimal> GetEthUsdPriceAsync(string engineAddress, CancellationToken cancellationToken = default)
    {
        var priceWei = await QueryAsync(engineAddress, new GetEthUsdPriceFunction(), cancellationToken);
        return Web3.Convert.FromWei(priceWei, Decimals);
    }

    public async Task<DepositResult> DepositCollateralAsync(string engineAddress, decimal amountEth, CancellationToken cancellationToken = default)
    {
        var function = new DepositCollateralFunction
        {
            AmountToSend = Web3.Convert.ToWei(amountEth, Decimals)
        };

        var receipt = await _web3Factory.Client.Eth
            .GetContractTransactionHandler<DepositCollateralFunction>()
            .SendRequestAndWaitForReceiptAsync(engineAddress, function)
            .WaitAsync(_timeout, cancellationToken);

        if (receipt.Status?.Value != 1)
        {
            throw new InvalidOperationException("Транзакція depositCollateral відхилена мережею (status = 0).");
        }

        return new DepositResult(amountEth, receipt.TransactionHash, receipt.GasUsed?.Value ?? BigInteger.Zero);
    }

    public async Task<PositionSnapshot> GetPositionAsync(string engineAddress, string user, CancellationToken cancellationToken = default)
    {
        var collateralWei = await QueryAsync(engineAddress, new CollateralDepositedFunction { User = user }, cancellationToken);
        var debtWei = await QueryAsync(engineAddress, new StablecoinMintedFunction { User = user }, cancellationToken);
        var collateralUsdWei = await QueryAsync(engineAddress, new GetCollateralValueInUsdFunction { User = user }, cancellationToken);
        var healthFactorWei = await QueryAsync(engineAddress, new GetHealthFactorFunction { User = user }, cancellationToken);

        decimal? healthFactor = healthFactorWei > InfinityThreshold
            ? null
            : Web3.Convert.FromWei(healthFactorWei, Decimals);

        return new PositionSnapshot(
            Web3.Convert.FromWei(collateralWei, Decimals),
            Web3.Convert.FromWei(collateralUsdWei, Decimals),
            Web3.Convert.FromWei(debtWei, Decimals),
            healthFactor,
            debtWei);
    }

    public async Task<MintResult> MintToHealthFactorAsync(string engineAddress, string user, decimal targetHealthFactor, CancellationToken cancellationToken = default)
    {
        var collateralWei = await QueryAsync(engineAddress, new CollateralDepositedFunction { User = user }, cancellationToken);
        var debtWei = await QueryAsync(engineAddress, new StablecoinMintedFunction { User = user }, cancellationToken);
        var priceWei = await QueryAsync(engineAddress, new GetEthUsdPriceFunction(), cancellationToken);
        var ratio = await QueryAsync(engineAddress, new CollateralizationRatioFunction(), cancellationToken);

        var collateralUsd = collateralWei * priceWei / Precision;
        var maxDebt = collateralUsd * RatioDenominator / ratio;
        var targetHf = Web3.Convert.ToWei(targetHealthFactor, Decimals);
        var targetDebt = maxDebt * Precision / targetHf;

        var toMint = targetDebt - debtWei;

        if (toMint <= BigInteger.Zero)
        {
            return new MintResult(0m, TransactionHash: null, BigInteger.Zero);
        }

        var receipt = await _web3Factory.Client.Eth
            .GetContractTransactionHandler<MintStablecoinFunction>()
            .SendRequestAndWaitForReceiptAsync(engineAddress, new MintStablecoinFunction { Amount = toMint })
            .WaitAsync(_timeout, cancellationToken);

        if (receipt.Status?.Value != 1)
        {
            throw new InvalidOperationException(
                "Транзакція mintStablecoin відхилена мережею. Перевірте, що власність на токен " +
                "стейблкоїна передано контракту StableEngine (transferOwnership) та що оракул " +
                "повертає свіжу ціну.");
        }

        return new MintResult(
            Web3.Convert.FromWei(toMint, Decimals),
            receipt.TransactionHash,
            receipt.GasUsed?.Value ?? BigInteger.Zero);
    }

    public async Task<InsolvencyResult> SimulateInsolvencyAsync(string engineAddress, string user, decimal percent, CancellationToken cancellationToken = default)
    {
        var collateralWei = await QueryAsync(engineAddress, new CollateralDepositedFunction { User = user }, cancellationToken);

        var basisPoints = new BigInteger(percent * 100m);
        var reduceWei = collateralWei * basisPoints / BasisPoints;

        if (reduceWei <= BigInteger.Zero)
        {
            throw new InvalidOperationException("Нема чого зменшувати: застава позичальника дорівнює нулю.");
        }

        var function = new SimulateInsolvencyFunction
        {
            User = user,
            Amount = reduceWei
        };

        var receipt = await _web3Factory.Client.Eth
            .GetContractTransactionHandler<SimulateInsolvencyFunction>()
            .SendRequestAndWaitForReceiptAsync(engineAddress, function)
            .WaitAsync(_timeout, cancellationToken);

        if (receipt.Status?.Value != 1)
        {
            throw new InvalidOperationException(
                "simulateInsolvency відхилено мережею: викликати бекдор може лише власник StableEngine " +
                "(акаунт, який розгортав контракт).");
        }

        return new InsolvencyResult(
            Web3.Convert.FromWei(reduceWei, Decimals),
            receipt.TransactionHash,
            receipt.GasUsed?.Value ?? BigInteger.Zero);
    }

    private async Task<BigInteger> QueryAsync<TFunction>(string engineAddress, TFunction function, CancellationToken cancellationToken)
        where TFunction : FunctionMessage, new()
    {
        return await _web3Factory.Client.Eth
            .GetContractQueryHandler<TFunction>()
            .QueryAsync<BigInteger>(engineAddress, function)
            .WaitAsync(_timeout, cancellationToken);
    }
}