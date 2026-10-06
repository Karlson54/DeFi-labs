using System.Numerics;

namespace DeFi.Models;

public sealed record StablecoinDeploymentResult(
    string Name,
    string Symbol,
    string Address,
    bool WasAlreadyDeployed,
    string? TransactionHash);

public sealed record EngineDeploymentResult(
    string Address,
    string StablecoinAddress,
    decimal InitialEthUsdPrice,
    bool WasAlreadyDeployed,
    string? TransactionHash);

public sealed record OwnershipTransferResult(
    string NewOwner,
    bool WasAlreadyTransferred,
    string? TransactionHash);

public sealed record PositionSnapshot(
    decimal CollateralEth,
    decimal CollateralUsd,
    decimal DebtUsd,
    decimal? HealthFactor,
    BigInteger DebtWei);

public sealed record DepositResult(
    decimal AmountEth,
    string TransactionHash,
    BigInteger GasUsed);

public sealed record MintResult(
    decimal Amount,
    string TransactionHash,
    BigInteger GasUsed);

public sealed record BurnResult(
    decimal Amount,
    string TransactionHash,
    BigInteger GasUsed);

public sealed record WithdrawAttemptResult(
    decimal AmountEth,
    bool Reverted,
    string? RevertReason,
    string? TransactionHash);

public sealed record ScenarioReport(
    string Network,
    string DeployerAddress,
    decimal EthUsdPrice,
    StablecoinDeploymentResult Stablecoin,
    EngineDeploymentResult Engine,
    OwnershipTransferResult Ownership,
    DepositResult Deposit,
    PositionSnapshot PositionAfterDeposit,
    MintResult Mint,
    PositionSnapshot PositionAfterMint,
    WithdrawAttemptResult BlockedWithdraw,
    PositionSnapshot PositionAfterBlocked,
    BurnResult Burn,
    WithdrawAttemptResult WithdrawAfterBurn,
    PositionSnapshot FinalPosition);