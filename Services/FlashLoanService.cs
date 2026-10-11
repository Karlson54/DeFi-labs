using System.Numerics;
using DeFi.Models;
using DeFi.Models.Contracts;
using Microsoft.Extensions.Options;
using Nethereum.ABI.FunctionEncoding;
using Nethereum.Contracts;
using Nethereum.JsonRpc.Client;
using Nethereum.Web3;

namespace DeFi.Services;

public interface IFlashLoanService
{
    Task<DeployedContract> DeployPoolAsync(string tokenA, string tokenB, CancellationToken cancellationToken = default);
    Task<string> AddLiquidityAsync(string pool, decimal amountA, decimal amountB, CancellationToken cancellationToken = default);
    Task<DeployedContract> DeploySpotOracleAsync(string pool, CancellationToken cancellationToken = default);
    Task<DeployedContract> DeployTrustedOracleAsync(decimal price, CancellationToken cancellationToken = default);
    Task<DeployedContract> DeployLenderAsync(string collateralToken, string borrowToken, string oracle, CancellationToken cancellationToken = default);
    Task<DeployedContract> DeployProviderAsync(string token, CancellationToken cancellationToken = default);
    Task<DeployedContract> DeployAttackerAsync(string provider, string pool, string lender, string tokenA, string tokenB, CancellationToken cancellationToken = default);

    Task<decimal> GetPriceAsync(string oracle, CancellationToken cancellationToken = default);
    Task<decimal> GetFlashFeeAsync(string provider, decimal amount, CancellationToken cancellationToken = default);
    Task<decimal> GetCollateralAsync(string lender, string user, CancellationToken cancellationToken = default);
    Task<decimal> GetDebtAsync(string lender, string user, CancellationToken cancellationToken = default);

    Task<AttackAttemptResult> TryAttackAsync(string attacker, decimal loanAmount, CancellationToken cancellationToken = default);

    Task<string> WithdrawProfitAsync(string attacker, string to, CancellationToken cancellationToken = default);
}

public sealed class FlashLoanService : IFlashLoanService
{
    private const int Decimals = 18;

    private readonly IWeb3Factory _web3Factory;
    private readonly IContractDeployer _deployer;
    private readonly TimeSpan _timeout;

    public FlashLoanService(IWeb3Factory web3Factory, IContractDeployer deployer, IOptions<Web3Settings> options)
    {
        _web3Factory = web3Factory;
        _deployer = deployer;
        _timeout = TimeSpan.FromSeconds(options.Value.TransactionTimeoutSeconds);
    }

    public Task<DeployedContract> DeployPoolAsync(string tokenA, string tokenB, CancellationToken cancellationToken = default) =>
        _deployer.DeployAsync<DexPoolDeployment>(
            "DexPool", m => { m.TokenA = tokenA; m.TokenB = tokenB; },
            "Скопіюйте артефакт DexPool.json з лаб. №3.", cancellationToken);

    public async Task<string> AddLiquidityAsync(string pool, decimal amountA, decimal amountB, CancellationToken cancellationToken = default)
    {
        var function = new PoolAddLiquidityFunction
        {
            AmountA = Web3.Convert.ToWei(amountA, Decimals),
            AmountB = Web3.Convert.ToWei(amountB, Decimals)
        };

        var receipt = await _web3Factory.Client.Eth
            .GetContractTransactionHandler<PoolAddLiquidityFunction>()
            .SendRequestAndWaitForReceiptAsync(pool, function)
            .WaitAsync(_timeout, cancellationToken);

        if (receipt.Status?.Value != 1)
        {
            throw new InvalidOperationException(
                "addLiquidity у пулі відхилено: перевірте approve обох токенів для адреси пулу.");
        }

        return receipt.TransactionHash;
    }

    public Task<DeployedContract> DeploySpotOracleAsync(string pool, CancellationToken cancellationToken = default) =>
        _deployer.DeployAsync<SpotPriceOracleDeployment>(
            "SpotPriceOracle", m => m.Pool = pool,
            "Перевірте адресу пулу та байткод SpotPriceOracle.json.", cancellationToken);

    public Task<DeployedContract> DeployTrustedOracleAsync(decimal price, CancellationToken cancellationToken = default) =>
        _deployer.DeployAsync<TrustedPriceOracleDeployment>(
            "TrustedPriceOracle", m => m.InitialPrice = Web3.Convert.ToWei(price, Decimals),
            "Перевірте байткод TrustedPriceOracle.json.", cancellationToken);

    public Task<DeployedContract> DeployLenderAsync(string collateralToken, string borrowToken, string oracle, CancellationToken cancellationToken = default) =>
        _deployer.DeployAsync<OracleLenderDeployment>(
            "OracleLender",
            m => { m.CollateralToken = collateralToken; m.BorrowToken = borrowToken; m.Oracle = oracle; },
            "Перевірте адреси токенів, оракула та байткод OracleLender.json.", cancellationToken);

    public Task<DeployedContract> DeployProviderAsync(string token, CancellationToken cancellationToken = default) =>
        _deployer.DeployAsync<FlashLoanProviderDeployment>(
            "FlashLoanProvider", m => m.Token = token,
            "Перевірте байткод FlashLoanProvider.json.", cancellationToken);

    public Task<DeployedContract> DeployAttackerAsync(
        string provider, string pool, string lender, string tokenA, string tokenB, CancellationToken cancellationToken = default) =>
        _deployer.DeployAsync<FlashLoanAttackerDeployment>(
            "FlashLoanAttacker",
            m => { m.Provider = provider; m.Pool = pool; m.Lender = lender; m.TokenA = tokenA; m.TokenB = tokenB; },
            "Перевірте адреси та байткод FlashLoanAttacker.json.", cancellationToken);

    public async Task<decimal> GetPriceAsync(string oracle, CancellationToken cancellationToken = default) =>
        Web3.Convert.FromWei(await QueryAsync(oracle, new GetPriceFunction(), cancellationToken), Decimals);

    public async Task<decimal> GetFlashFeeAsync(string provider, decimal amount, CancellationToken cancellationToken = default) =>
        Web3.Convert.FromWei(
            await QueryAsync(provider, new FlashFeeFunction { Amount = Web3.Convert.ToWei(amount, Decimals) }, cancellationToken),
            Decimals);

    public async Task<decimal> GetCollateralAsync(string lender, string user, CancellationToken cancellationToken = default) =>
        Web3.Convert.FromWei(await QueryAsync(lender, new LenderCollateralFunction { User = user }, cancellationToken), Decimals);

    public async Task<decimal> GetDebtAsync(string lender, string user, CancellationToken cancellationToken = default) =>
        Web3.Convert.FromWei(await QueryAsync(lender, new LenderDebtFunction { User = user }, cancellationToken), Decimals);

    public async Task<AttackAttemptResult> TryAttackAsync(string attacker, decimal loanAmount, CancellationToken cancellationToken = default)
    {
        var function = new AttackFunction { LoanAmount = Web3.Convert.ToWei(loanAmount, Decimals) };

        try
        {
            var receipt = await _web3Factory.Client.Eth
                .GetContractTransactionHandler<AttackFunction>()
                .SendRequestAndWaitForReceiptAsync(attacker, function)
                .WaitAsync(_timeout, cancellationToken);

            if (receipt.Status?.Value != 1)
            {
                return new AttackAttemptResult(true, "транзакцію відхилено (status = 0)", receipt.TransactionHash, BigInteger.Zero);
            }

            return new AttackAttemptResult(false, null, receipt.TransactionHash, receipt.GasUsed?.Value ?? BigInteger.Zero);
        }
        catch (RpcResponseException ex)
        {
            return new AttackAttemptResult(true, RevertReasonParser.Extract(ex.Message), null, BigInteger.Zero);
        }
        catch (SmartContractRevertException ex)
        {
            return new AttackAttemptResult(true, RevertReasonParser.Extract(ex.Message), null, BigInteger.Zero);
        }
        catch (SmartContractCustomErrorRevertException ex)
        {
            return new AttackAttemptResult(true, RevertReasonParser.Extract(ex.Message), null, BigInteger.Zero);
        }
    }

    public async Task<string> WithdrawProfitAsync(string attacker, string to, CancellationToken cancellationToken = default)
    {
        var receipt = await _web3Factory.Client.Eth
            .GetContractTransactionHandler<WithdrawProfitFunction>()
            .SendRequestAndWaitForReceiptAsync(attacker, new WithdrawProfitFunction { To = to })
            .WaitAsync(_timeout, cancellationToken);

        if (receipt.Status?.Value != 1)
        {
            throw new InvalidOperationException("withdrawProfit відхилено мережею (status = 0).");
        }

        return receipt.TransactionHash;
    }

    private async Task<BigInteger> QueryAsync<TFunction>(string address, TFunction function, CancellationToken cancellationToken)
        where TFunction : FunctionMessage, new() =>
        await _web3Factory.Client.Eth
            .GetContractQueryHandler<TFunction>()
            .QueryAsync<BigInteger>(address, function)
            .WaitAsync(_timeout, cancellationToken);
}