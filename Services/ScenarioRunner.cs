using DeFi.Models;
using Microsoft.Extensions.Options;

namespace DeFi.Services;

public interface IScenarioRunner
{
    Task<ScenarioReport> RunAsync(CancellationToken cancellationToken = default);
}

public sealed class ScenarioRunner : IScenarioRunner
{
    private readonly IWeb3Factory _web3Factory;
    private readonly IStablecoinService _stablecoinService;
    private readonly IStableEngineService _engineService;
    private readonly IDeploymentStateStore _stateStore;
    private readonly Lab5Settings _settings;

    public ScenarioRunner(
        IWeb3Factory web3Factory,
        IStablecoinService stablecoinService,
        IStableEngineService engineService,
        IDeploymentStateStore stateStore,
        IOptions<Lab5Settings> settingsOptions)
    {
        _web3Factory = web3Factory;
        _stablecoinService = stablecoinService;
        _engineService = engineService;
        _stateStore = stateStore;
        _settings = settingsOptions.Value;
    }

    public async Task<ScenarioReport> RunAsync(CancellationToken cancellationToken = default)
    {
        ValidateSettings();

        var user = _web3Factory.AccountAddress;
        var chainId = _web3Factory.ChainId;

        var state = await _stateStore.LoadAsync(chainId, user, cancellationToken);

        var coin = await EnsureStablecoinAsync(state.StablecoinAddress, cancellationToken);
        state = state with { StablecoinAddress = coin.Address };
        await _stateStore.SaveAsync(state, cancellationToken);

        var knownEngine = coin.WasAlreadyDeployed ? state.EngineAddress : null;
        var engine = await EnsureEngineAsync(knownEngine, coin.Address, cancellationToken);
        state = state with { EngineAddress = engine.Address };
        await _stateStore.SaveAsync(state, cancellationToken);

        await _engineService.EnsurePriceAsync(engine.Address, _settings.InitialEthUsdPrice, cancellationToken);

        var ownership = await _stablecoinService.TransferOwnershipAsync(coin.Address, engine.Address, cancellationToken);

        var deposit = await _engineService.DepositCollateralAsync(engine.Address, _settings.CollateralEth, cancellationToken);
        var afterDeposit = await _engineService.GetPositionAsync(engine.Address, user, cancellationToken);

        var mint = await _engineService.MintMaxAsync(engine.Address, user, cancellationToken);
        var afterMint = await _engineService.GetPositionAsync(engine.Address, user, cancellationToken);

        var blocked = await _engineService.TryWithdrawCollateralAsync(engine.Address, _settings.WithdrawEth, cancellationToken);
        if (!blocked.Reverted)
        {
            throw new InvalidOperationException(
                "КРИТИЧНО: зняття застави при Health Factor = 1 пройшло успішно. " +
                "Інваріант безпеки не працює — перевірте виклик _revertIfHealthFactorIsBroken у withdrawCollateral.");
        }
        var afterBlocked = await _engineService.GetPositionAsync(engine.Address, user, cancellationToken);

        await _stablecoinService.ApproveAsync(coin.Address, engine.Address, afterBlocked.DebtWei, cancellationToken);
        var burn = await _engineService.BurnAsync(engine.Address, afterBlocked.DebtWei, cancellationToken);

        var withdrawAfterBurn = await _engineService.TryWithdrawCollateralAsync(engine.Address, _settings.WithdrawEth, cancellationToken);
        if (withdrawAfterBurn.Reverted)
        {
            throw new InvalidOperationException(
                $"Після погашення боргу зняття застави несподівано відхилено: {withdrawAfterBurn.RevertReason}");
        }
        var final = await _engineService.GetPositionAsync(engine.Address, user, cancellationToken);

        return new ScenarioReport(
            Network: $"chainId {chainId}",
            DeployerAddress: user,
            EthUsdPrice: _settings.InitialEthUsdPrice,
            Stablecoin: coin,
            Engine: engine,
            Ownership: ownership,
            Deposit: deposit,
            PositionAfterDeposit: afterDeposit,
            Mint: mint,
            PositionAfterMint: afterMint,
            BlockedWithdraw: blocked,
            PositionAfterBlocked: afterBlocked,
            Burn: burn,
            WithdrawAfterBurn: withdrawAfterBurn,
            FinalPosition: final);
    }

    private void ValidateSettings()
    {
        if (_settings.InitialEthUsdPrice <= 0 || _settings.CollateralEth <= 0 || _settings.WithdrawEth <= 0)
        {
            throw new InvalidOperationException(
                "У Lab5Settings мають бути додатними InitialEthUsdPrice, CollateralEth та WithdrawEth.");
        }

        if (string.IsNullOrWhiteSpace(_settings.Stablecoin.Name) || string.IsNullOrWhiteSpace(_settings.Stablecoin.Symbol))
        {
            throw new InvalidOperationException("У Lab5Settings:Stablecoin мають бути задані Name та Symbol.");
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
                knownAddress, stablecoinAddress, _settings.InitialEthUsdPrice,
                WasAlreadyDeployed: true, TransactionHash: null);
        }

        return await _engineService.DeployAsync(stablecoinAddress, _settings.InitialEthUsdPrice, cancellationToken);
    }

    private async Task<bool> HasContractCodeAsync(string address)
    {
        var code = await _web3Factory.Client.Eth.GetCode.SendRequestAsync(address);
        return !string.IsNullOrEmpty(code) && code != "0x";
    }
}