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
    string PriceFeedAddress,
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
    string? TransactionHash,
    BigInteger GasUsed);

public sealed record MintResult(
    decimal Amount,
    string? TransactionHash,
    BigInteger GasUsed);

public sealed record LiquidatorFundingResult(
    string LiquidatorAddress,
    decimal EthSent,
    string? EthTransactionHash,
    decimal EthBalance,
    decimal StableSent,
    string? StableTransactionHash,
    decimal StableBalance);

public sealed record ScenarioReport(
    string Network,
    string DeployerAddress,
    string LiquidatorAddress,
    decimal EthUsdPrice,
    StablecoinDeploymentResult Stablecoin,
    EngineDeploymentResult Engine,
    OwnershipTransferResult Ownership,
    DepositResult Deposit,
    PositionSnapshot PositionAfterDeposit,
    MintResult Mint,
    PositionSnapshot PositionAfterMint,
    LiquidatorFundingResult Funding);

public sealed record InsolvencyResult(
    decimal ReducedEth,
    string TransactionHash,
    BigInteger GasUsed);

public sealed record CrashReport(
    string Network,
    string OwnerAddress,
    string EngineAddress,
    decimal EthUsdPrice,
    decimal Percent,
    PositionSnapshot Before,
    InsolvencyResult Reduce,
    PositionSnapshot After);

public sealed record LiquidationResult(
    string User,
    string TransactionHash,
    BigInteger BlockNumber,
    decimal DebtCovered,
    decimal CollateralSeizedEth,
    BigInteger GasUsed);