using System.Numerics;
using DeFi.Models;
using Microsoft.Extensions.Options;
using Nethereum.Web3;

namespace DeFi.Services;

public interface IScenarioRunner
{
    Task<ScenarioReport> RunAsync(CancellationToken cancellationToken = default);

    Task<StandReport> PrepareStandAsync(CancellationToken cancellationToken = default);

    Task<DonationReport> DonateRewardAsync(CancellationToken cancellationToken = default);
}

public sealed class ScenarioRunner : IScenarioRunner
{
    private const int Decimals = 18;

    private readonly IWeb3Factory _web3Factory;
    private readonly ITokenService _tokenService;
    private readonly IRouterService _routerService;
    private readonly IVaultService _vaultService;
    private readonly IDeploymentStateStore _stateStore;
    private readonly Lab7Settings _settings;

    public ScenarioRunner(
        IWeb3Factory web3Factory,
        ITokenService tokenService,
        IRouterService routerService,
        IVaultService vaultService,
        IDeploymentStateStore stateStore,
        IOptions<Lab7Settings> settingsOptions)
    {
        _web3Factory = web3Factory;
        _tokenService = tokenService;
        _routerService = routerService;
        _vaultService = vaultService;
        _stateStore = stateStore;
        _settings = settingsOptions.Value;
    }

    private sealed record Infrastructure(
        TokenDeploymentResult TokenA,
        TokenDeploymentResult TokenB,
        LiquidityResult? Pool,
        VaultDeploymentResult Vault);

    public async Task<ScenarioReport> RunAsync(CancellationToken cancellationToken = default)
    {
        ValidateSettings();

        var user = _web3Factory.AccountAddress;
        var infra = await PrepareInfrastructureAsync(user, cancellationToken);
        var vault = infra.Vault.Address;

        var (deposit, sharesWei, afterDeposit) = await DepositStepAsync(infra, user, cancellationToken);

        var reward = await TransferRewardAsync(infra, cancellationToken);
        var afterReward = await _vaultService.GetSnapshotAsync(vault, user, cancellationToken);

        var rewardWei = await _tokenService.BalanceOfAsync(infra.TokenB.Address, vault, cancellationToken);
        var compound = await _vaultService.CompoundAsync(vault, rewardWei, cancellationToken);
        var afterCompound = await _vaultService.GetSnapshotAsync(vault, user, cancellationToken);

        var balanceBefore = await _tokenService.BalanceOfAsync(infra.TokenA.Address, user, cancellationToken);
        var withdrawTx = await _vaultService.WithdrawAsync(vault, sharesWei, cancellationToken);
        var balanceAfter = await _tokenService.BalanceOfAsync(infra.TokenA.Address, user, cancellationToken);

        var withdraw = new WithdrawResult(
            Web3.Convert.FromWei(sharesWei, Decimals),
            Web3.Convert.FromWei(balanceAfter - balanceBefore, Decimals),
            withdrawTx.TransactionHash,
            withdrawTx.GasUsed);

        var final = await _vaultService.GetSnapshotAsync(vault, user, cancellationToken);

        if (withdraw.Assets <= deposit.Assets)
        {
            throw new InvalidOperationException(
                "КРИТИЧНО: після compound() інвестор отримав не більше, ніж вніс. " +
                "Перевірте, що compound() обміняв винагороду на базовий актив і залишив її у сховищі (to = address(this)).");
        }

        return new ScenarioReport(
            Network: $"chainId {_web3Factory.ChainId}",
            UserAddress: user,
            RouterAddress: _settings.RouterAddress,
            TokenA: infra.TokenA,
            TokenB: infra.TokenB,
            Pool: infra.Pool,
            Vault: infra.Vault,
            Deposit: deposit,
            PositionAfterDeposit: afterDeposit,
            Reward: reward,
            PositionAfterReward: afterReward,
            Compound: compound,
            PositionAfterCompound: afterCompound,
            Withdraw: withdraw,
            FinalPosition: final);
    }

    public async Task<StandReport> PrepareStandAsync(CancellationToken cancellationToken = default)
    {
        ValidateSettings();

        var user = _web3Factory.AccountAddress;
        var infra = await PrepareInfrastructureAsync(user, cancellationToken);
        var (deposit, _, afterDeposit) = await DepositStepAsync(infra, user, cancellationToken);

        return new StandReport(
            Network: $"chainId {_web3Factory.ChainId}",
            UserAddress: user,
            RouterAddress: _settings.RouterAddress,
            TokenA: infra.TokenA,
            TokenB: infra.TokenB,
            Pool: infra.Pool,
            Vault: infra.Vault,
            Deposit: deposit,
            PositionAfterDeposit: afterDeposit);
    }

    public async Task<DonationReport> DonateRewardAsync(CancellationToken cancellationToken = default)
    {
        ValidateSettings();

        var user = _web3Factory.AccountAddress;
        var state = await _stateStore.LoadAsync(_web3Factory.ChainId, user, cancellationToken);

        if (string.IsNullOrWhiteSpace(state.VaultAddress) || string.IsNullOrWhiteSpace(state.TokenBAddress) ||
            !await HasContractCodeAsync(state.VaultAddress))
        {
            throw new InvalidOperationException(
                "Сховище ще не розгорнуте. Спочатку підготуйте стенд: dotnet run -- --stand");
        }

        var tx = await _tokenService.TransferAsync(state.TokenBAddress!, state.VaultAddress!, _settings.RewardAmount, cancellationToken);
        var balanceWei = await _tokenService.BalanceOfAsync(state.TokenBAddress!, state.VaultAddress!, cancellationToken);

        return new DonationReport(
            Network: $"chainId {_web3Factory.ChainId}",
            VaultAddress: state.VaultAddress!,
            RewardSymbol: _settings.TokenB.Symbol,
            Reward: new RewardTransferResult(_settings.RewardAmount, Web3.Convert.FromWei(balanceWei, Decimals), tx));
    }

    private async Task<Infrastructure> PrepareInfrastructureAsync(string user, CancellationToken cancellationToken)
    {
        var state = await _stateStore.LoadAsync(_web3Factory.ChainId, user, cancellationToken);

        var tokenA = await EnsureTokenAsync(_settings.TokenA, state.TokenAAddress, cancellationToken);
        var tokenB = await EnsureTokenAsync(_settings.TokenB, state.TokenBAddress, cancellationToken);

        var tokensChanged = !tokenA.WasAlreadyDeployed || !tokenB.WasAlreadyDeployed;
        var routerChanged = !string.Equals(state.RouterAddress, _settings.RouterAddress, StringComparison.OrdinalIgnoreCase);

        if (tokensChanged || routerChanged)
        {
            state = state with { VaultAddress = null, LiquidityProvided = false };
        }

        state = state with
        {
            TokenAAddress = tokenA.Address,
            TokenBAddress = tokenB.Address,
            RouterAddress = _settings.RouterAddress
        };
        await _stateStore.SaveAsync(state, cancellationToken);

        LiquidityResult? pool = null;
        if (!state.LiquidityProvided)
        {
            await _tokenService.ApproveAsync(tokenA.Address, _settings.RouterAddress, _settings.PoolLiquidityA, cancellationToken);
            await _tokenService.ApproveAsync(tokenB.Address, _settings.RouterAddress, _settings.PoolLiquidityB, cancellationToken);

            pool = await _routerService.AddLiquidityAsync(
                tokenA.Address, tokenB.Address,
                _settings.PoolLiquidityA, _settings.PoolLiquidityB,
                user, cancellationToken);

            state = state with { LiquidityProvided = true };
            await _stateStore.SaveAsync(state, cancellationToken);
        }

        var vault = await EnsureVaultAsync(state.VaultAddress, tokenA.Address, tokenB.Address, cancellationToken);
        state = state with { VaultAddress = vault.Address };
        await _stateStore.SaveAsync(state, cancellationToken);

        return new Infrastructure(tokenA, tokenB, pool, vault);
    }

    private async Task<(DepositResult Deposit, BigInteger SharesWei, VaultSnapshot After)> DepositStepAsync(
        Infrastructure infra, string user, CancellationToken cancellationToken)
    {
        var vault = infra.Vault.Address;

        var before = await _vaultService.GetSnapshotAsync(vault, user, cancellationToken);

        await _tokenService.ApproveAsync(infra.TokenA.Address, vault, _settings.DepositAmount, cancellationToken);
        var tx = await _vaultService.DepositAsync(vault, _settings.DepositAmount, cancellationToken);

        var after = await _vaultService.GetSnapshotAsync(vault, user, cancellationToken);

        var sharesWei = after.SharesWei - before.SharesWei;

        var deposit = new DepositResult(
            _settings.DepositAmount,
            Web3.Convert.FromWei(sharesWei, Decimals),
            tx.TransactionHash,
            tx.GasUsed);

        return (deposit, sharesWei, after);
    }

    private async Task<RewardTransferResult> TransferRewardAsync(Infrastructure infra, CancellationToken cancellationToken)
    {
        var tx = await _tokenService.TransferAsync(
            infra.TokenB.Address, infra.Vault.Address, _settings.RewardAmount, cancellationToken);

        var balanceWei = await _tokenService.BalanceOfAsync(infra.TokenB.Address, infra.Vault.Address, cancellationToken);

        return new RewardTransferResult(_settings.RewardAmount, Web3.Convert.FromWei(balanceWei, Decimals), tx);
    }

    private void ValidateSettings()
    {
        if (string.IsNullOrWhiteSpace(_settings.RouterAddress) ||
            _settings.RouterAddress.StartsWith("0x_", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "У appsettings.json не задано Lab7Settings:RouterAddress — адресу Router-контракту " +
                "Uniswap V2 (або сумісного форку) у тій мережі, куди виконується деплой.");
        }

        if (_settings.DepositAmount <= 0 || _settings.RewardAmount <= 0 ||
            _settings.PoolLiquidityA <= 0 || _settings.PoolLiquidityB <= 0)
        {
            throw new InvalidOperationException(
                "У Lab7Settings мають бути додатними DepositAmount, RewardAmount, PoolLiquidityA та PoolLiquidityB.");
        }

        if (string.IsNullOrWhiteSpace(_settings.TokenA.Symbol) || string.IsNullOrWhiteSpace(_settings.TokenB.Symbol))
        {
            throw new InvalidOperationException("У Lab7Settings:TokenA та TokenB мають бути задані Name і Symbol.");
        }
    }

    private async Task<TokenDeploymentResult> EnsureTokenAsync(TokenSettings settings, string? knownAddress, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(knownAddress) && await HasContractCodeAsync(knownAddress))
        {
            return await _tokenService.DescribeAsync(settings, knownAddress, cancellationToken);
        }

        return await _tokenService.DeployAsync(settings, cancellationToken);
    }

    private async Task<VaultDeploymentResult> EnsureVaultAsync(string? knownAddress, string assetAddress, string rewardAddress, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(knownAddress) && await HasContractCodeAsync(knownAddress))
        {
            return new VaultDeploymentResult(
                knownAddress, assetAddress, rewardAddress, _settings.RouterAddress,
                WasAlreadyDeployed: true, TransactionHash: null);
        }

        return await _vaultService.DeployAsync(assetAddress, rewardAddress, _settings.RouterAddress, cancellationToken);
    }

    private async Task<bool> HasContractCodeAsync(string address)
    {
        var code = await _web3Factory.Client.Eth.GetCode.SendRequestAsync(address);
        return !string.IsNullOrEmpty(code) && code != "0x";
    }
}