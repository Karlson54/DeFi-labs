using Defi.Models;
using Microsoft.Extensions.Options;

namespace Defi.Services;

public interface IIntegrationRunner
{
    Task<IntegrationReport> RunAsync(CancellationToken cancellationToken = default);
}

public sealed class IntegrationRunner : IIntegrationRunner
{
    private readonly IWeb3Factory _web3Factory;
    private readonly ITokenService _tokenService;
    private readonly IDefiIntegratorService _integratorService;
    private readonly IDeploymentStateStore _stateStore;
    private readonly Lab4Settings _settings;

    public IntegrationRunner(
        IWeb3Factory web3Factory,
        ITokenService tokenService,
        IDefiIntegratorService integratorService,
        IDeploymentStateStore stateStore,
        IOptions<Lab4Settings> settingsOptions)
    {
        _web3Factory = web3Factory;
        _tokenService = tokenService;
        _integratorService = integratorService;
        _stateStore = stateStore;
        _settings = settingsOptions.Value;
    }

    public async Task<IntegrationReport> RunAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.RouterAddress) ||
            _settings.RouterAddress.StartsWith("0x_", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "У appsettings.json не задано Lab4Settings:RouterAddress — адресу Router-контракту " +
                "Uniswap V2 (або сумісного форку) у тій мережі, куди виконується деплой.");
        }

        var deployer = _web3Factory.AccountAddress;
        var chainId = _web3Factory.ChainId;

        var state = await _stateStore.LoadAsync(chainId, deployer, cancellationToken);

        var tokenA = await EnsureTokenAsync(_settings.TokenA, state.TokenAAddress, cancellationToken);
        state = state with { TokenAAddress = tokenA.Address };
        await _stateStore.SaveAsync(state, cancellationToken);

        var tokenB = await EnsureTokenAsync(_settings.TokenB, state.TokenBAddress, cancellationToken);
        state = state with { TokenBAddress = tokenB.Address };
        await _stateStore.SaveAsync(state, cancellationToken);

        var integrator = await EnsureIntegratorAsync(state.IntegratorAddress, cancellationToken);
        state = state with { IntegratorAddress = integrator.Address };
        await _stateStore.SaveAsync(state, cancellationToken);

        // Approve на суму ліквідності + окремий approve на суму свопу (він виконується
        // після того, як частина токенів A вже пішла в пул через provideLiquidity).
        await _tokenService.ApproveAsync(tokenA.Address, integrator.Address, _settings.LiquidityAmountA, cancellationToken);
        await _tokenService.ApproveAsync(tokenB.Address, integrator.Address, _settings.LiquidityAmountB, cancellationToken);

        var liquidity = await _integratorService.ProvideLiquidityAsync(
            integrator.Address, tokenA.Address, tokenB.Address,
            _settings.LiquidityAmountA, _settings.LiquidityAmountB, cancellationToken);

        await _tokenService.ApproveAsync(tokenA.Address, integrator.Address, _settings.SwapAmountIn, cancellationToken);

        var swap = await _integratorService.SwapTokensAsync(
            integrator.Address, tokenA.Address, tokenB.Address,
            _settings.SwapAmountIn, _settings.SwapAmountOutMin, cancellationToken);

        return new IntegrationReport(
            Network: $"chainId {chainId}",
            DeployerAddress: deployer,
            RouterAddress: _settings.RouterAddress,
            TokenA: tokenA,
            TokenB: tokenB,
            Integrator: integrator,
            Liquidity: liquidity,
            Swap: swap);
    }

    private async Task<TokenDeploymentResult> EnsureTokenAsync(TokenSettings settings, string? knownAddress, CancellationToken cancellationToken)
    {
        return string.IsNullOrWhiteSpace(knownAddress)
            ? await _tokenService.DeployAsync(settings, cancellationToken)
            : await _tokenService.DescribeAsync(settings, knownAddress!, cancellationToken);
    }

    private async Task<IntegratorDeploymentResult> EnsureIntegratorAsync(string? knownAddress, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(knownAddress))
        {
            return new IntegratorDeploymentResult(knownAddress, _settings.RouterAddress, WasAlreadyDeployed: true, TransactionHash: null);
        }

        return await _integratorService.DeployAsync(_settings.RouterAddress, cancellationToken);
    }
}