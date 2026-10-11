using System.Numerics;

namespace DeFi.Models;

public sealed record TokenDeploymentResult(
    string Name,
    string Symbol,
    string Address,
    decimal TotalSupply,
    bool WasAlreadyDeployed,
    string? TransactionHash);

public sealed record ContractRef(
    string Name,
    string Address,
    bool WasAlreadyDeployed,
    string? TransactionHash);

public sealed record OwnershipTransferResult(
    string NewOwner,
    bool WasAlreadyTransferred,
    string? TransactionHash);

public sealed record DirectCallAttemptResult(
    bool Reverted,
    string? RevertReason,
    string? TransactionHash);

public sealed record TxResult(string TransactionHash, BigInteger GasUsed);

public sealed record ProposalCreatedResult(
    BigInteger ProposalId,
    string Payload,
    DateTimeOffset Deadline,
    string TransactionHash,
    BigInteger GasUsed);

public sealed record ProposalSnapshot(
    BigInteger Id,
    string Target,
    string Description,
    DateTimeOffset Deadline,
    decimal VotesFor,
    decimal VotesAgainst,
    bool Executed);

public sealed record VoteResult(
    decimal Weight,
    bool Support,
    string TransactionHash,
    BigInteger GasUsed);

public sealed record ScenarioReport(
    string Network,
    string UserAddress,
    string TokenSymbol,
    ContractRef Token,
    ContractRef Protocol,
    ContractRef Dao,
    OwnershipTransferResult Ownership,
    DirectCallAttemptResult DirectAttempt,
    BigInteger FeeBefore,
    BigInteger NewFee,
    ProposalCreatedResult Proposal,
    VoteResult Vote,
    ProposalSnapshot AfterVote,
    TimeSpan Waited,
    TxResult Execute,
    ProposalSnapshot AfterExecute,
    BigInteger FeeAfter);