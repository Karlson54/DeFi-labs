using System.Numerics;
using DeFi.Models;
using Microsoft.Extensions.Options;
using Nethereum.Web3;

namespace DeFi.Services;

public interface IScenarioRunner
{
    Task<ScenarioReport> RunAsync(CancellationToken cancellationToken = default);

    Task<CrashReport> SimulateCrashAsync(CancellationToken cancellationToken = default);
}

public sealed class ScenarioRunner : IScenarioRunner
{
    private const int Decimals = 18;

    private readonly IWeb3Factory _web3Factory;
    private readonly IStablecoinService _stablecoinService;
    private readonly IStableEngineService _engineService;
    private readonly IWalletService _walletService;
    private readonly ILiquidatorService _liquidator;
    private readonly IDeploymentStateStore _stateStore;
    private readonly Lab6Settings _settings;

    public ScenarioRunner(
        IWeb3Factory web3Factory,
        IStablecoinService stablecoinService,
        IStableEngineService engineService,
        IWalletService walletService,
        ILiquidatorService liquidator,
        IDeploymentStateStore stateStore,
        IOptions<Lab6Settings> settingsOptions)
    {
        _web3Factory = web3Factory;
        _stablecoinService = stablecoinService;
        _engineService = engineService;
        _walletService = walletService;
        _liquidator = liquidator;
        _stateStore = stateStore;
        _settings = settingsOptions.Value;
    }

    public async Task<ScenarioReport> RunAsync(CancellationToken cancellationToken = default)
    {
        ValidateSettings();

        var user = _web3Factory.AccountAddress;
        var chainId = _web3Factory.ChainId;
        var liquidatorAddress = _liquidator.Address;

        if (string.Equals(user, liquidatorAddress, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Lab6Settings:LiquidatorPrivateKey збігається з Web3Settings:PrivateKey. " +
                "Ліквідатор і позичальник мають бути різними акаунтами (самоліквідація заборонена).");
        }

        var state = await _stateStore.LoadAsync(chainId, user, cancellationToken);

        var coin = await EnsureStablecoinAsync(state.StablecoinAddress, cancellationToken);
        state = state with { StablecoinAddress = coin.Address };
        await _stateStore.SaveAsync(state, cancellationToken);

        var knownEngine = coin.WasAlreadyDeployed ? state.EngineAddress : null;
        var engine = await EnsureEngineAsync(knownEngine, coin.Address, cancellationToken);
        state = state with { EngineAddress = engine.Address };
        await _stateStore.SaveAsync(state, cancellationToken);

        var ownership = await _stablecoinService.TransferOwnershipAsync(coin.Address, engine.Address, cancellationToken);

        var price = await _engineService.GetEthUsdPriceAsync(engine.Address, cancellationToken);

        var before = await _engineService.GetPositionAsync(engine.Address, user, cancellationToken);
        var missingEth = _settings.CollateralEth - before.CollateralEth;

        var deposit = missingEth > 0
            ? await _engineService.DepositCollateralAsync(engine.Address, missingEth, cancellationToken)
            : new DepositResult(0m, TransactionHash: null, BigInteger.Zero);
        var afterDeposit = await _engineService.GetPositionAsync(engine.Address, user, cancellationToken);

        var mint = await _engineService.MintToHealthFactorAsync(
            engine.Address, user, _settings.TargetHealthFactor, cancellationToken);
        var afterMint = await _engineService.GetPositionAsync(engine.Address, user, cancellationToken);

        var funding = await EnsureLiquidatorFundedAsync(coin.Address, afterMint.DebtWei, liquidatorAddress, cancellationToken);

        return new ScenarioReport(
            Network: $"chainId {chainId}",
            DeployerAddress: user,
            LiquidatorAddress: liquidatorAddress,
            EthUsdPrice: price,
            Stablecoin: coin,
            Engine: engine,
            Ownership: ownership,
            Deposit: deposit,
            PositionAfterDeposit: afterDeposit,
            Mint: mint,
            PositionAfterMint: afterMint,
            Funding: funding);
    }

    public async Task<CrashReport> SimulateCrashAsync(CancellationToken cancellationToken = default)
    {
        ValidateSettings();

        var owner = _web3Factory.AccountAddress;
        var chainId = _web3Factory.ChainId;

        var state = await _stateStore.LoadAsync(chainId, owner, cancellationToken);

        if (string.IsNullOrWhiteSpace(state.EngineAddress) || !await HasContractCodeAsync(state.EngineAddress))
        {
            throw new InvalidOperationException(
                "Кредитне ядро ще не розгорнуте. Спочатку виконайте підготовку стенду: dotnet run");
        }

        var engine = state.EngineAddress!;

        var before = await _engineService.GetPositionAsync(engine, owner, cancellationToken);

        if (before.DebtWei.IsZero)
        {
            throw new InvalidOperationException(
                "У позичальника немає боргу — ліквідувати нічого. Виконайте підготовку стенду: dotnet run");
        }

        var price = await _engineService.GetEthUsdPriceAsync(engine, cancellationToken);
        var reduce = await _engineService.SimulateInsolvencyAsync(
            engine, owner, _settings.CrashCollateralPercent, cancellationToken);
        var after = await _engineService.GetPositionAsync(engine, owner, cancellationToken);

        return new CrashReport(
            Network: $"chainId {chainId}",
            OwnerAddress: owner,
            EngineAddress: engine,
            EthUsdPrice: price,
            Percent: _settings.CrashCollateralPercent,
            Before: before,
            Reduce: reduce,
            After: after);
    }

    private void ValidateSettings()
    {
        if (_settings.CollateralEth <= 0)
        {
            throw new InvalidOperationException("У Lab6Settings:CollateralEth має бути додатне значення.");
        }

        if (_settings.TargetHealthFactor < 1m)
        {
            throw new InvalidOperationException("Lab6Settings:TargetHealthFactor не може бути меншим за 1.");
        }

        if (_settings.CrashCollateralPercent <= 0 || _settings.CrashCollateralPercent >= 100)
        {
            throw new InvalidOperationException("Lab6Settings:CrashCollateralPercent має бути в діапазоні (0; 100).");
        }

        if (_settings.TargetHealthFactor * (1m - _settings.CrashCollateralPercent / 100m) >= 1m)
        {
            throw new InvalidOperationException(
                "При таких TargetHealthFactor і CrashCollateralPercent позиція не стане неплатоспроможною " +
                "(HF залишиться ≥ 1). Збільште CrashCollateralPercent.");
        }

        if (string.IsNullOrWhiteSpace(_settings.PriceFeedAddress))
        {
            throw new InvalidOperationException("У Lab6Settings:PriceFeedAddress не задано адресу Chainlink Data Feed.");
        }

        if (string.IsNullOrWhiteSpace(_settings.Stablecoin.Name) || string.IsNullOrWhiteSpace(_settings.Stablecoin.Symbol))
        {
            throw new InvalidOperationException("У Lab6Settings:Stablecoin мають бути задані Name та Symbol.");
        }
    }

    private async Task<StablecoinDeploymentResult> EnsureStablecoinAsync(string? knownAddress, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(knownAddress) && await HasContractCodeAsync(knownAddress))
        {
            return _stablecoinService.Describe(_settings.Stablecoin, knownAddress);
        }

        return await _stablecoinService.DeployAsync(_settings.Stablecoin, cancellationToken);
    }

    private async Task<EngineDeploymentResult> EnsureEngineAsync(string? knownAddress, string stablecoinAddress, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(knownAddress) && await HasContractCodeAsync(knownAddress))
        {
            return new EngineDeploymentResult(
                knownAddress, stablecoinAddress, _settings.PriceFeedAddress,
                WasAlreadyDeployed: true, TransactionHash: null);
        }

        return await _engineService.DeployAsync(stablecoinAddress, _settings.PriceFeedAddress, cancellationToken);
    }

    private async Task<LiquidatorFundingResult> EnsureLiquidatorFundedAsync(
        string stablecoinAddress, BigInteger debtWei, string liquidatorAddress, CancellationToken cancellationToken)
    {
        var ethBalance = await _walletService.GetEthBalanceAsync(liquidatorAddress, cancellationToken);
        decimal ethSent = 0m;
        string? ethTx = null;

        if (ethBalance < _settings.LiquidatorMinEth)
        {
            ethSent = _settings.LiquidatorMinEth - ethBalance;
            ethTx = await _walletService.TransferEthAsync(liquidatorAddress, ethSent, cancellationToken);
        }

        var stableBalanceWei = await _liquidator.GetStablecoinBalanceAsync(stablecoinAddress, cancellationToken);
        var stableSentWei = BigInteger.Zero;
        string? stableTx = null;

        if (stableBalanceWei < debtWei)
        {
            stableSentWei = debtWei - stableBalanceWei;
            stableTx = await _stablecoinService.TransferAsync(stablecoinAddress, liquidatorAddress, stableSentWei, cancellationToken);
        }

        var ethAfter = await _walletService.GetEthBalanceAsync(liquidatorAddress, cancellationToken);
        var stableAfterWei = await _liquidator.GetStablecoinBalanceAsync(stablecoinAddress, cancellationToken);

        return new LiquidatorFundingResult(
            liquidatorAddress,
            ethSent, ethTx, ethAfter,
            Web3.Convert.FromWei(stableSentWei, Decimals), stableTx,
            Web3.Convert.FromWei(stableAfterWei, Decimals));
    }

    private async Task<bool> HasContractCodeAsync(string address)
    {
        var code = await _web3Factory.Client.Eth.GetCode.SendRequestAsync(address);
        return !string.IsNullOrEmpty(code) && code != "0x";
    }
}