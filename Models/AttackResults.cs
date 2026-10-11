using System.Numerics;

namespace DeFi.Models;

public sealed record AttackAttemptResult(
    bool Reverted,
    string? RevertReason,
    string? TransactionHash,
    BigInteger GasUsed);

public sealed record AttackOutcome(
    bool Reverted,
    string? RevertReason,
    string? TransactionHash,
    BigInteger GasUsed,
    decimal ProfitB,
    decimal LenderBalanceBefore,
    decimal LenderBalanceAfter,
    decimal DebtOpened,
    decimal CollateralLocked,
    string? ProfitWithdrawTransactionHash);

public sealed record AttackReport(
    string Network,
    string DeployerAddress,
    string SymbolA,
    string SymbolB,
    ContractRef TokenA,
    ContractRef TokenB,
    ContractRef Pool,
    ContractRef Provider,
    ContractRef SpotOracle,
    ContractRef TrustedOracle,
    ContractRef VulnerableLender,
    ContractRef SafeLender,
    ContractRef VulnerableAttacker,
    ContractRef SafeAttacker,
    decimal SpotPriceBefore,
    decimal SpotPriceAfter,
    decimal TrustedPrice,
    decimal LoanAmount,
    decimal FlashFee,
    AttackOutcome Vulnerable,
    AttackOutcome Protected);