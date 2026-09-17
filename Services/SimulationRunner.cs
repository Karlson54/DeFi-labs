using DeFi.Models;
using Microsoft.Extensions.Options;
using Nethereum.Web3;

namespace DeFi.Services;

public interface ISimulationRunner
{
    Task<SimulationReport> RunAsync(CancellationToken cancellationToken = default);
}

public sealed class SimulationRunner : ISimulationRunner
{
    private readonly IWeb3Factory _web3Factory;
    private readonly ITokenService _tokenService;
    private readonly IDexPoolService _poolService;
    private readonly IDeploymentStateStore _stateStore;
    private readonly SimulationSettings _simulation;

    public SimulationRunner(
        IWeb3Factory web3Factory,
        ITokenService tokenService,
        IDexPoolService poolService,
        IDeploymentStateStore stateStore,
        IOptions<SimulationSettings> simulationOptions)
    {
        _web3Factory = web3Factory;
        _tokenService = tokenService;
        _poolService = poolService;
        _stateStore = stateStore;
        _simulation = simulationOptions.Value;
    }

    public async Task<SimulationReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var deployer = _web3Factory.AccountAddress;
        var chainId = _web3Factory.ChainId;

        var nativeBalance = await _web3Factory.Client.Eth.GetBalance.SendRequestAsync(deployer);
        var state = await _stateStore.LoadAsync(chainId, deployer, cancellationToken);

        var tokenA = await EnsureTokenAsync(_simulation.TokenA, state.TokenAAddress, cancellationToken);
        state = state with { TokenAAddress = tokenA.Address };
        await _stateStore.SaveAsync(state, cancellationToken);

        var tokenB = await EnsureTokenAsync(_simulation.TokenB, state.TokenBAddress, cancellationToken);
        state = state with { TokenBAddress = tokenB.Address };
        await _stateStore.SaveAsync(state, cancellationToken);

        PoolDeploymentResult pool;

        if (!string.IsNullOrWhiteSpace(state.PoolAddress))
        {
            pool = new PoolDeploymentResult(state.PoolAddress!, tokenA.Address, tokenB.Address, true, null);
        }
        else
        {
            pool = await _poolService.DeployAsync(tokenA.Address, tokenB.Address, cancellationToken);
            state = state with { PoolAddress = pool.Address };
            await _stateStore.SaveAsync(state, cancellationToken);
        }

        var liquidity = await EnsureLiquidityAsync(pool, tokenA.Address, tokenB.Address, cancellationToken);

        var feeTiers = await ProbeFeeTiersAsync(pool.Address, liquidity.PoolState, cancellationToken);

        await _tokenService.ApproveAsync(tokenA.Address, pool.Address, _simulation.SwapAmountA, cancellationToken);
        var swap = await _poolService.SwapAForBAsync(
            pool.Address,
            _simulation.SwapAmountA,
            _simulation.SlippageTolerancePercent,
            cancellationToken);

        var partnerTransfer = await TransferToPartnerAsync(tokenA, cancellationToken);

        return new SimulationReport(
            Network: $"chainId {chainId}",
            DeployerAddress: deployer,
            DeployerNativeBalance: Web3.Convert.FromWei(nativeBalance.Value),
            TokenA: tokenA,
            TokenB: tokenB,
            Pool: pool,
            Liquidity: liquidity,
            Swap: swap,
            PartnerTransfer: partnerTransfer,
            FeeTiers: feeTiers);
    }

    private async Task<TokenDeploymentResult> EnsureTokenAsync(TokenSettings settings, string? knownAddress, CancellationToken cancellationToken)
    {
        return string.IsNullOrWhiteSpace(knownAddress)
            ? await _tokenService.DeployAsync(settings, cancellationToken)
            : await _tokenService.DescribeAsync(settings, knownAddress!, cancellationToken);
    }

    private async Task<LiquidityResult> EnsureLiquidityAsync(
        PoolDeploymentResult pool,
        string tokenA,
        string tokenB,
        CancellationToken cancellationToken)
    {
        var current = await _poolService.GetStateAsync(pool.Address, cancellationToken);

        if (current.ReserveA > 0 && current.ReserveB > 0)
        {
            return new LiquidityResult(0, 0, current, string.Empty, Skipped: true);
        }

        await _tokenService.ApproveAsync(tokenA, pool.Address, _simulation.LiquidityAmountA, cancellationToken);
        await _tokenService.ApproveAsync(tokenB, pool.Address, _simulation.LiquidityAmountB, cancellationToken);

        var hash = await _poolService.AddLiquidityAsync(
            pool.Address,
            _simulation.LiquidityAmountA,
            _simulation.LiquidityAmountB,
            cancellationToken);

        var state = await _poolService.GetStateAsync(pool.Address, cancellationToken);

        return new LiquidityResult(_simulation.LiquidityAmountA, _simulation.LiquidityAmountB, state, hash, Skipped: false);
    }

    private async Task<IReadOnlyList<FeeTierProbe>> ProbeFeeTiersAsync(
        string poolAddress,
        PoolStateSnapshot state,
        CancellationToken cancellationToken)
    {
        var shares = new[] { 0.005m, 0.03m, 0.07m, 0.20m };
        var probes = new List<FeeTierProbe>();

        foreach (var share in shares)
        {
            var amountIn = Math.Round(state.ReserveA * share, 4);

            if (amountIn <= 0)
            {
                continue;
            }

            var feeBps = await _poolService.GetFeeBpsAsync(poolAddress, amountIn, state.ReserveA, cancellationToken);
            var amountOut = await _poolService.QuoteAsync(poolAddress, amountIn, state.ReserveA, state.ReserveB, cancellationToken);

            probes.Add(new FeeTierProbe(amountIn, feeBps, amountOut));
        }

        return probes;
    }

    private async Task<PartnerTransferResult> TransferToPartnerAsync(TokenDeploymentResult tokenA, CancellationToken cancellationToken)
    {
        var partner = _simulation.PartnerAddress?.Trim();

        if (string.IsNullOrWhiteSpace(partner))
        {
            return new PartnerTransferResult(true, null, 0, tokenA.Symbol, null);
        }

        var hash = await _tokenService.TransferAsync(tokenA.Address, partner, _simulation.PartnerTransferAmount, cancellationToken);

        return new PartnerTransferResult(false, partner, _simulation.PartnerTransferAmount, tokenA.Symbol, hash);
    }
}
