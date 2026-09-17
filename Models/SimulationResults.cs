using System.Numerics;

namespace DeFi.Models;

public sealed record TokenDeploymentResult(
    string Name,
    string Symbol,
    string Address,
    decimal TotalSupply,
    bool WasAlreadyDeployed,
    string? TransactionHash);

public sealed record PoolDeploymentResult(
    string Address,
    string TokenAAddress,
    string TokenBAddress,
    bool WasAlreadyDeployed,
    string? TransactionHash);

public sealed record PoolStateSnapshot(decimal ReserveA, decimal ReserveB)
{
    public decimal ConstantK => ReserveA * ReserveB;

    public decimal PriceAInB => ReserveA == 0 ? 0 : ReserveB / ReserveA;
}

public sealed record LiquidityResult(
    decimal AmountA,
    decimal AmountB,
    PoolStateSnapshot PoolState,
    string TransactionHash,
    bool Skipped);

public sealed record SwapResult(
    decimal AmountIn,
    decimal AmountOut,
    decimal QuotedAmountOut,
    decimal MinAmountOut,
    int FeeBps,
    PoolStateSnapshot StateBefore,
    PoolStateSnapshot StateAfter,
    string TransactionHash,
    BigInteger GasUsed)
{
    public decimal EffectivePrice => AmountIn == 0 ? 0 : AmountOut / AmountIn;

    public decimal SlippagePercent =>
        StateBefore.PriceAInB == 0
            ? 0
            : (StateBefore.PriceAInB - EffectivePrice) / StateBefore.PriceAInB * 100m;

    public decimal ConstantKGrowthPercent =>
        StateBefore.ConstantK == 0
            ? 0
            : (StateAfter.ConstantK - StateBefore.ConstantK) / StateBefore.ConstantK * 100m;
}

public sealed record PartnerTransferResult(
    bool Skipped,
    string? PartnerAddress,
    decimal Amount,
    string Symbol,
    string? TransactionHash);

public sealed record SimulationReport(
    string Network,
    string DeployerAddress,
    decimal DeployerNativeBalance,
    TokenDeploymentResult TokenA,
    TokenDeploymentResult TokenB,
    PoolDeploymentResult Pool,
    LiquidityResult Liquidity,
    SwapResult Swap,
    PartnerTransferResult PartnerTransfer,
    IReadOnlyList<FeeTierProbe> FeeTiers);

public sealed record FeeTierProbe(decimal AmountIn, int FeeBps, decimal AmountOut);
