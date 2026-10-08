using System.Numerics;
using DeFi.Models;
using DeFi.Models.Contracts;
using Microsoft.Extensions.Options;
using Nethereum.Contracts;
using Nethereum.Web3;

namespace DeFi.Services;

public interface ILiquidatorService
{
    string Address { get; }

    Task<decimal> GetEthUsdPriceAsync(string engineAddress, CancellationToken cancellationToken = default);

    Task<BigInteger?> GetHealthFactorAsync(string engineAddress, string user, CancellationToken cancellationToken = default);

    Task<BigInteger> GetDebtAsync(string engineAddress, string user, CancellationToken cancellationToken = default);

    Task<BigInteger> GetStablecoinBalanceAsync(string tokenAddress, CancellationToken cancellationToken = default);

    Task<string> ApproveAsync(string tokenAddress, string spender, BigInteger amountWei, CancellationToken cancellationToken = default);

    Task<LiquidationResult> LiquidateAsync(string engineAddress, string user, CancellationToken cancellationToken = default);
}

public sealed class LiquidatorService : ILiquidatorService
{
    private const int Decimals = 18;
    private static readonly BigInteger InfinityThreshold = BigInteger.Pow(10, 30);
    private const string KeySettingName = "Lab6Settings:LiquidatorPrivateKey";

    private readonly Lazy<Web3> _client;
    private readonly Lazy<string> _address;
    private readonly TimeSpan _timeout;

    public LiquidatorService(IWeb3Factory web3Factory, IOptions<Web3Settings> web3Options, IOptions<Lab6Settings> labOptions)
    {
        _timeout = TimeSpan.FromSeconds(web3Options.Value.TransactionTimeoutSeconds);

        var key = labOptions.Value.LiquidatorPrivateKey;
        _client = new Lazy<Web3>(() => web3Factory.CreateClient(key, KeySettingName));
        _address = new Lazy<string>(() => web3Factory.GetAddress(key, KeySettingName));
    }

    public string Address => _address.Value;

    private Web3 Client => _client.Value;

    public async Task<decimal> GetEthUsdPriceAsync(string engineAddress, CancellationToken cancellationToken = default)
    {
        var priceWei = await QueryAsync(engineAddress, new GetEthUsdPriceFunction(), cancellationToken);
        return Web3.Convert.FromWei(priceWei, Decimals);
    }

    public async Task<BigInteger?> GetHealthFactorAsync(string engineAddress, string user, CancellationToken cancellationToken = default)
    {
        var hf = await QueryAsync(engineAddress, new GetHealthFactorFunction { User = user }, cancellationToken);
        return hf > InfinityThreshold ? null : hf;
    }

    public Task<BigInteger> GetDebtAsync(string engineAddress, string user, CancellationToken cancellationToken = default) =>
        QueryAsync(engineAddress, new StablecoinMintedFunction { User = user }, cancellationToken);

    public Task<BigInteger> GetStablecoinBalanceAsync(string tokenAddress, CancellationToken cancellationToken = default) =>
        QueryAsync(tokenAddress, new StableCoinBalanceOfFunction { Account = Address }, cancellationToken);

    public async Task<string> ApproveAsync(string tokenAddress, string spender, BigInteger amountWei, CancellationToken cancellationToken = default)
    {
        var function = new StableCoinApproveFunction
        {
            Spender = spender,
            Amount = amountWei
        };

        var receipt = await Client.Eth
            .GetContractTransactionHandler<StableCoinApproveFunction>()
            .SendRequestAndWaitForReceiptAsync(tokenAddress, function)
            .WaitAsync(_timeout, cancellationToken);

        if (receipt.Status?.Value != 1)
        {
            throw new InvalidOperationException(
                $"Approve ліквідатора на токен {tokenAddress} для {spender} відхилено мережею (status = 0).");
        }

        return receipt.TransactionHash;
    }

    public async Task<LiquidationResult> LiquidateAsync(string engineAddress, string user, CancellationToken cancellationToken = default)
    {
        var debtWei = await GetDebtAsync(engineAddress, user, cancellationToken);
        var collateralBefore = await QueryAsync(engineAddress, new CollateralDepositedFunction { User = user }, cancellationToken);

        var receipt = await Client.Eth
            .GetContractTransactionHandler<LiquidateFunction>()
            .SendRequestAndWaitForReceiptAsync(engineAddress, new LiquidateFunction { User = user })
            .WaitAsync(_timeout, cancellationToken);

        if (receipt.Status?.Value != 1)
        {
            throw new InvalidOperationException(
                "Транзакція liquidate відхилена мережею. Найімовірніше, позицію вже ліквідував " +
                "інший агент, HF знову піднявся вище 1, або в ліквідатора немає approve/балансу стейблкоїнів.");
        }

        var collateralAfter = await QueryAsync(engineAddress, new CollateralDepositedFunction { User = user }, cancellationToken);

        return new LiquidationResult(
            user,
            receipt.TransactionHash,
            receipt.BlockNumber?.Value ?? BigInteger.Zero,
            Web3.Convert.FromWei(debtWei, Decimals),
            Web3.Convert.FromWei(collateralBefore - collateralAfter, Decimals),
            receipt.GasUsed?.Value ?? BigInteger.Zero);
    }

    private async Task<BigInteger> QueryAsync<TFunction>(string contractAddress, TFunction function, CancellationToken cancellationToken)
        where TFunction : FunctionMessage, new()
    {
        return await Client.Eth
            .GetContractQueryHandler<TFunction>()
            .QueryAsync<BigInteger>(contractAddress, function)
            .WaitAsync(_timeout, cancellationToken);
    }
}