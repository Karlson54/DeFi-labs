using System.Numerics;

namespace DeFi.Models;

public sealed record TokenDeploymentResult(
    string Name,
    string Symbol,
    string Address,
    decimal TotalSupply,
    bool WasAlreadyDeployed,
    string? TransactionHash);

public sealed record LiquidityResult(
    decimal AmountA,
    decimal AmountB,
    string TransactionHash,
    BigInteger GasUsed);

public sealed record VaultDeploymentResult(
    string Address,
    string AssetAddress,
    string RewardTokenAddress,
    string RouterAddress,
    bool WasAlreadyDeployed,
    string? TransactionHash);

public sealed record VaultTxResult(string TransactionHash, BigInteger GasUsed);

public sealed record VaultSnapshot(
    BigInteger SharesWei,
    decimal Shares,
    decimal AssetsValue,
    decimal TotalAssets,
    decimal TotalShares,
    decimal SharePrice);

public sealed record DepositResult(
    decimal Assets,
    decimal Shares,
    string TransactionHash,
    BigInteger GasUsed);

public sealed record RewardTransferResult(
    decimal Amount,
    decimal RewardBalanceInVault,
    string TransactionHash);

public sealed record CompoundResult(
    decimal RewardSold,
    decimal AssetsReceived,
    decimal TotalAssetsBefore,
    decimal TotalAssetsAfter,
    string TransactionHash,
    BigInteger GasUsed);

public sealed record WithdrawResult(
    decimal Shares,
    decimal Assets,
    string TransactionHash,
    BigInteger GasUsed);

public sealed record ScenarioReport(
    string Network,
    string UserAddress,
    string RouterAddress,
    TokenDeploymentResult TokenA,
    TokenDeploymentResult TokenB,
    LiquidityResult? Pool,
    VaultDeploymentResult Vault,
    DepositResult Deposit,
    VaultSnapshot PositionAfterDeposit,
    RewardTransferResult Reward,
    VaultSnapshot PositionAfterReward,
    CompoundResult Compound,
    VaultSnapshot PositionAfterCompound,
    WithdrawResult Withdraw,
    VaultSnapshot FinalPosition);

public sealed record StandReport(
    string Network,
    string UserAddress,
    string RouterAddress,
    TokenDeploymentResult TokenA,
    TokenDeploymentResult TokenB,
    LiquidityResult? Pool,
    VaultDeploymentResult Vault,
    DepositResult Deposit,
    VaultSnapshot PositionAfterDeposit);

public sealed record DonationReport(
    string Network,
    string VaultAddress,
    string RewardSymbol,
    RewardTransferResult Reward);