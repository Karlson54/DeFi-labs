using System.Numerics;
using System.Text.RegularExpressions;
using DeFi.Models;
using DeFi.Models.Contracts;
using Microsoft.Extensions.Options;
using Nethereum.ABI.FunctionEncoding;
using Nethereum.Contracts;
using Nethereum.JsonRpc.Client;
using Nethereum.Web3;

namespace DeFi.Services;

public interface IStableEngineService
{
    Task<EngineDeploymentResult> DeployAsync(string stablecoinAddress, decimal initialEthUsdPrice, CancellationToken cancellationToken = default);

    Task EnsurePriceAsync(string engineAddress, decimal ethUsdPrice, CancellationToken cancellationToken = default);

    Task<DepositResult> DepositCollateralAsync(string engineAddress, decimal amountEth, CancellationToken cancellationToken = default);

    Task<PositionSnapshot> GetPositionAsync(string engineAddress, string user, CancellationToken cancellationToken = default);

    Task<MintResult> MintMaxAsync(string engineAddress, string user, CancellationToken cancellationToken = default);

    Task<BurnResult> BurnAsync(string engineAddress, BigInteger amountWei, CancellationToken cancellationToken = default);

    Task<WithdrawAttemptResult> TryWithdrawCollateralAsync(string engineAddress, decimal amountEth, CancellationToken cancellationToken = default);
}

public sealed class StableEngineService : IStableEngineService
{
    private const int Decimals = 18;

    private const int RatioDenominator = 100;
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

    public async Task<EngineDeploymentResult> DeployAsync(string stablecoinAddress, decimal initialEthUsdPrice, CancellationToken cancellationToken = default)
    {
        var artifact = await _artifacts.GetAsync("StableEngine", cancellationToken);

        var deployment = new StableEngineDeployment(artifact.Bytecode)
        {
            Stablecoin = stablecoinAddress,
            InitialPrice = Web3.Convert.ToWei(initialEthUsdPrice, Decimals)
        };

        var receipt = await _web3Factory.Client.Eth
            .GetContractDeploymentHandler<StableEngineDeployment>()
            .SendRequestAndWaitForReceiptAsync(deployment)
            .WaitAsync(_timeout, cancellationToken);

        if (receipt.Status?.Value != 1)
        {
            throw new InvalidOperationException(
                "Розгортання StableEngine відхилено мережею (status = 0). " +
                "Перевірте адресу стейблкоїна та коректність байткоду артефакту.");
        }

        return new EngineDeploymentResult(
            receipt.ContractAddress, stablecoinAddress, initialEthUsdPrice,
            WasAlreadyDeployed: false, receipt.TransactionHash);
    }

    public async Task EnsurePriceAsync(string engineAddress, decimal ethUsdPrice, CancellationToken cancellationToken = default)
    {
        var expected = Web3.Convert.ToWei(ethUsdPrice, Decimals);

        var current = await _web3Factory.Client.Eth
            .GetContractQueryHandler<MockEthUsdPriceFunction>()
            .QueryAsync<BigInteger>(engineAddress, new MockEthUsdPriceFunction())
            .WaitAsync(_timeout, cancellationToken);

        if (current == expected)
        {
            return;
        }

        var receipt = await _web3Factory.Client.Eth
            .GetContractTransactionHandler<SetMockEthUsdPriceFunction>()
            .SendRequestAndWaitForReceiptAsync(engineAddress, new SetMockEthUsdPriceFunction { NewPrice = expected })
            .WaitAsync(_timeout, cancellationToken);

        if (receipt.Status?.Value != 1)
        {
            throw new InvalidOperationException("setMockEthUsdPrice відхилено мережею: змінювати ціну може лише власник рушія.");
        }
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

    public async Task<MintResult> MintMaxAsync(string engineAddress, string user, CancellationToken cancellationToken = default)
    {
        var collateralWei = await QueryAsync(engineAddress, new CollateralDepositedFunction { User = user }, cancellationToken);
        var debtWei = await QueryAsync(engineAddress, new StablecoinMintedFunction { User = user }, cancellationToken);
        var priceWei = await QueryAsync(engineAddress, new MockEthUsdPriceFunction(), cancellationToken);
        var ratio = await QueryAsync(engineAddress, new CollateralizationRatioFunction(), cancellationToken);

        var collateralUsd = collateralWei * priceWei / Precision;
        var maxDebt = collateralUsd * RatioDenominator / ratio;
        var toMint = maxDebt - debtWei;

        if (toMint <= BigInteger.Zero)
        {
            throw new InvalidOperationException(
                "Запасу для нової емісії немає: позиція вже на межі Health Factor = 1.");
        }

        var receipt = await _web3Factory.Client.Eth
            .GetContractTransactionHandler<MintStablecoinFunction>()
            .SendRequestAndWaitForReceiptAsync(engineAddress, new MintStablecoinFunction { Amount = toMint })
            .WaitAsync(_timeout, cancellationToken);

        if (receipt.Status?.Value != 1)
        {
            throw new InvalidOperationException(
                "Транзакція mintStablecoin відхилена мережею. Перевірте, що власність на токен " +
                "стейблкоїна передано контракту StableEngine (transferOwnership).");
        }

        return new MintResult(
            Web3.Convert.FromWei(toMint, Decimals),
            receipt.TransactionHash,
            receipt.GasUsed?.Value ?? BigInteger.Zero);
    }

    public async Task<BurnResult> BurnAsync(string engineAddress, BigInteger amountWei, CancellationToken cancellationToken = default)
    {
        var receipt = await _web3Factory.Client.Eth
            .GetContractTransactionHandler<BurnStablecoinFunction>()
            .SendRequestAndWaitForReceiptAsync(engineAddress, new BurnStablecoinFunction { Amount = amountWei })
            .WaitAsync(_timeout, cancellationToken);

        if (receipt.Status?.Value != 1)
        {
            throw new InvalidOperationException(
                "Транзакція burnStablecoin відхилена мережею. Найімовірніша причина — не виконано approve " +
                "на потрібну суму в контракті стейблкоїна для адреси StableEngine.");
        }

        return new BurnResult(
            Web3.Convert.FromWei(amountWei, Decimals),
            receipt.TransactionHash,
            receipt.GasUsed?.Value ?? BigInteger.Zero);
    }

    public async Task<WithdrawAttemptResult> TryWithdrawCollateralAsync(string engineAddress, decimal amountEth, CancellationToken cancellationToken = default)
    {
        var function = new WithdrawCollateralFunction
        {
            Amount = Web3.Convert.ToWei(amountEth, Decimals)
        };

        try
        {
            var receipt = await _web3Factory.Client.Eth
                .GetContractTransactionHandler<WithdrawCollateralFunction>()
                .SendRequestAndWaitForReceiptAsync(engineAddress, function)
                .WaitAsync(_timeout, cancellationToken);

            if (receipt.Status?.Value != 1)
            {
                return new WithdrawAttemptResult(amountEth, Reverted: true, "транзакцію відхилено (status = 0)", receipt.TransactionHash);
            }

            return new WithdrawAttemptResult(amountEth, Reverted: false, RevertReason: null, receipt.TransactionHash);
        }
        catch (RpcResponseException ex)
        {
            return new WithdrawAttemptResult(amountEth, Reverted: true, ExtractReason(ex.Message), TransactionHash: null);
        }
        catch (SmartContractRevertException ex)
        {
            return new WithdrawAttemptResult(amountEth, Reverted: true, ExtractReason(ex.Message), TransactionHash: null);
        }
    }

    private async Task<BigInteger> QueryAsync<TFunction>(string engineAddress, TFunction function, CancellationToken cancellationToken)
        where TFunction : FunctionMessage, new()
    {
        return await _web3Factory.Client.Eth
            .GetContractQueryHandler<TFunction>()
            .QueryAsync<BigInteger>(engineAddress, function)
            .WaitAsync(_timeout, cancellationToken);
    }

    private static string ExtractReason(string message)
    {
        var match = Regex.Match(message, @"StableEngine: [^'""\r\n]+");
        return match.Success ? match.Value.Trim() : message;
    }
}