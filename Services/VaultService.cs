using System.Numerics;
using DeFi.Models;
using DeFi.Models.Contracts;
using Microsoft.Extensions.Options;
using Nethereum.Contracts;
using Nethereum.Web3;

namespace DeFi.Services;

public interface IVaultService
{
    Task<VaultDeploymentResult> DeployAsync(string assetAddress, string rewardTokenAddress, string routerAddress, CancellationToken cancellationToken = default);

    Task<VaultTxResult> DepositAsync(string vaultAddress, decimal assets, CancellationToken cancellationToken = default);

    Task<VaultTxResult> WithdrawAsync(string vaultAddress, BigInteger sharesWei, CancellationToken cancellationToken = default);

    Task<CompoundResult> CompoundAsync(string vaultAddress, BigInteger rewardWei, CancellationToken cancellationToken = default);

    Task<VaultSnapshot> GetSnapshotAsync(string vaultAddress, string account, CancellationToken cancellationToken = default);
}

public sealed class VaultService : IVaultService
{
    private const int Decimals = 18;
    private static readonly BigInteger Precision = BigInteger.Pow(10, Decimals);

    private readonly IWeb3Factory _web3Factory;
    private readonly IContractArtifactProvider _artifacts;
    private readonly TimeSpan _timeout;

    public VaultService(IWeb3Factory web3Factory, IContractArtifactProvider artifacts, IOptions<Web3Settings> options)
    {
        _web3Factory = web3Factory;
        _artifacts = artifacts;
        _timeout = TimeSpan.FromSeconds(options.Value.TransactionTimeoutSeconds);
    }

    public async Task<VaultDeploymentResult> DeployAsync(string assetAddress, string rewardTokenAddress, string routerAddress, CancellationToken cancellationToken = default)
    {
        var artifact = await _artifacts.GetAsync("AutoCompoundVault", cancellationToken);

        var deployment = new AutoCompoundVaultDeployment(artifact.Bytecode)
        {
            Asset = assetAddress,
            RewardToken = rewardTokenAddress,
            Router = routerAddress
        };

        var receipt = await _web3Factory.Client.Eth
            .GetContractDeploymentHandler<AutoCompoundVaultDeployment>()
            .SendRequestAndWaitForReceiptAsync(deployment)
            .WaitAsync(_timeout, cancellationToken);

        if (receipt.Status?.Value != 1)
        {
            throw new InvalidOperationException(
                "Розгортання AutoCompoundVault відхилено мережею (status = 0). " +
                "Перевірте адреси токенів і Router-а та коректність байткоду артефакту.");
        }

        return new VaultDeploymentResult(
            receipt.ContractAddress, assetAddress, rewardTokenAddress, routerAddress,
            WasAlreadyDeployed: false, receipt.TransactionHash);
    }

    public async Task<VaultTxResult> DepositAsync(string vaultAddress, decimal assets, CancellationToken cancellationToken = default)
    {
        var function = new VaultDepositFunction { Assets = Web3.Convert.ToWei(assets, Decimals) };

        var receipt = await _web3Factory.Client.Eth
            .GetContractTransactionHandler<VaultDepositFunction>()
            .SendRequestAndWaitForReceiptAsync(vaultAddress, function)
            .WaitAsync(_timeout, cancellationToken);

        if (receipt.Status?.Value != 1)
        {
            throw new InvalidOperationException(
                "Транзакція deposit відхилена мережею. Найімовірніша причина — не виконано approve " +
                "на потрібну суму в контракті базового активу для адреси сховища.");
        }

        return new VaultTxResult(receipt.TransactionHash, receipt.GasUsed?.Value ?? BigInteger.Zero);
    }

    public async Task<VaultTxResult> WithdrawAsync(string vaultAddress, BigInteger sharesWei, CancellationToken cancellationToken = default)
    {
        var receipt = await _web3Factory.Client.Eth
            .GetContractTransactionHandler<VaultWithdrawFunction>()
            .SendRequestAndWaitForReceiptAsync(vaultAddress, new VaultWithdrawFunction { Shares = sharesWei })
            .WaitAsync(_timeout, cancellationToken);

        if (receipt.Status?.Value != 1)
        {
            throw new InvalidOperationException(
                "Транзакція withdraw відхилена мережею. Перевірте, що на балансі достатньо акцій сховища.");
        }

        return new VaultTxResult(receipt.TransactionHash, receipt.GasUsed?.Value ?? BigInteger.Zero);
    }

    public async Task<CompoundResult> CompoundAsync(string vaultAddress, BigInteger rewardWei, CancellationToken cancellationToken = default)
    {
        var assetsBefore = await QueryAsync(vaultAddress, new TotalAssetsFunction(), cancellationToken);

        var receipt = await _web3Factory.Client.Eth
            .GetContractTransactionHandler<CompoundFunction>()
            .SendRequestAndWaitForReceiptAsync(vaultAddress, new CompoundFunction())
            .WaitAsync(_timeout, cancellationToken);

        if (receipt.Status?.Value != 1)
        {
            throw new InvalidOperationException(
                "Транзакція compound відхилена мережею. Найімовірніші причини: у сховища немає винагороди, " +
                "у Router-а немає пулу «Токен B -> Токен A» (спершу додайте ліквідність) або це не той Router.");
        }

        var assetsAfter = await QueryAsync(vaultAddress, new TotalAssetsFunction(), cancellationToken);

        return new CompoundResult(
            Web3.Convert.FromWei(rewardWei, Decimals),
            Web3.Convert.FromWei(assetsAfter - assetsBefore, Decimals),
            Web3.Convert.FromWei(assetsBefore, Decimals),
            Web3.Convert.FromWei(assetsAfter, Decimals),
            receipt.TransactionHash,
            receipt.GasUsed?.Value ?? BigInteger.Zero);
    }

    public async Task<VaultSnapshot> GetSnapshotAsync(string vaultAddress, string account, CancellationToken cancellationToken = default)
    {
        var sharesWei = await QueryAsync(vaultAddress, new VaultBalanceOfFunction { Account = account }, cancellationToken);
        var assetsWei = await QueryAsync(vaultAddress, new ConvertToAssetsFunction { Shares = sharesWei }, cancellationToken);
        var totalAssetsWei = await QueryAsync(vaultAddress, new TotalAssetsFunction(), cancellationToken);
        var totalSharesWei = await QueryAsync(vaultAddress, new VaultTotalSupplyFunction(), cancellationToken);

        var priceWei = await QueryAsync(vaultAddress, new ConvertToAssetsFunction { Shares = Precision }, cancellationToken);

        return new VaultSnapshot(
            sharesWei,
            Web3.Convert.FromWei(sharesWei, Decimals),
            Web3.Convert.FromWei(assetsWei, Decimals),
            Web3.Convert.FromWei(totalAssetsWei, Decimals),
            Web3.Convert.FromWei(totalSharesWei, Decimals),
            Web3.Convert.FromWei(priceWei, Decimals));
    }

    private async Task<BigInteger> QueryAsync<TFunction>(string contractAddress, TFunction function, CancellationToken cancellationToken)
        where TFunction : FunctionMessage, new()
    {
        return await _web3Factory.Client.Eth
            .GetContractQueryHandler<TFunction>()
            .QueryAsync<BigInteger>(contractAddress, function)
            .WaitAsync(_timeout, cancellationToken);
    }
}