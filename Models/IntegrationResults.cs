using System.Numerics;

namespace Defi.Models;

public sealed record TokenDeploymentResult(
    string Name,
    string Symbol,
    string Address,
    decimal TotalSupply,
    bool WasAlreadyDeployed,
    string? TransactionHash);

public sealed record IntegratorDeploymentResult(
    string Address,
    string RouterAddress,
    bool WasAlreadyDeployed,
    string? TransactionHash);

public sealed record LiquidityResult(
    decimal AmountA,
    decimal AmountB,
    string TransactionHash,
    BigInteger GasUsed);

public sealed record SwapResult(
    decimal AmountIn,
    decimal AmountOutMin,
    string TransactionHash,
    BigInteger GasUsed);

public sealed record IntegrationReport(
    string Network,
    string DeployerAddress,
    string RouterAddress,
    TokenDeploymentResult TokenA,
    TokenDeploymentResult TokenB,
    IntegratorDeploymentResult Integrator,
    LiquidityResult Liquidity,
    SwapResult Swap);