using System.Numerics;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace DeFi.Models.Contracts;

public class MockProtocolDeployment : ContractDeploymentMessage
{
    public MockProtocolDeployment() : base(string.Empty) { }

    [Parameter("uint256", "initialFee_", 1)]
    public BigInteger InitialFee { get; set; }
}

[Function("fee", "uint256")]
public class FeeFunction : FunctionMessage
{
}

[Function("setFee")]
public class SetFeeFunction : FunctionMessage
{
    [Parameter("uint256", "newFee", 1)]
    public BigInteger NewFee { get; set; }
}

public class SimpleDaoDeployment : ContractDeploymentMessage
{
    public SimpleDaoDeployment() : base(string.Empty) { }

    [Parameter("address", "governanceToken_", 1)]
    public string GovernanceToken { get; set; } = string.Empty;
}

[Function("createProposal", "uint256")]
public class CreateProposalFunction : FunctionMessage
{
    [Parameter("address", "_targetContract", 1)]
    public string TargetContract { get; set; } = string.Empty;

    [Parameter("bytes", "_payload", 2)]
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    [Parameter("string", "_description", 3)]
    public string Description { get; set; } = string.Empty;
}

[Function("vote")]
public class VoteFunction : FunctionMessage
{
    [Parameter("uint256", "proposalId", 1)]
    public BigInteger ProposalId { get; set; }

    [Parameter("bool", "support", 2)]
    public bool Support { get; set; }
}

[Function("executeProposal")]
public class ExecuteProposalFunction : FunctionMessage
{
    [Parameter("uint256", "proposalId", 1)]
    public BigInteger ProposalId { get; set; }
}

[Function("proposalCount", "uint256")]
public class ProposalCountFunction : FunctionMessage
{
}

[Function("proposals")]
public class ProposalsFunction : FunctionMessage
{
    [Parameter("uint256", "proposalId", 1)]
    public BigInteger ProposalId { get; set; }
}

[FunctionOutput]
public class ProposalOutputDto : IFunctionOutputDTO
{
    [Parameter("address", "target", 1)]
    public string Target { get; set; } = string.Empty;

    [Parameter("bytes", "payload", 2)]
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    [Parameter("string", "description", 3)]
    public string Description { get; set; } = string.Empty;

    [Parameter("uint256", "deadline", 4)]
    public BigInteger Deadline { get; set; }

    [Parameter("uint256", "votesFor", 5)]
    public BigInteger VotesFor { get; set; }

    [Parameter("uint256", "votesAgainst", 6)]
    public BigInteger VotesAgainst { get; set; }

    [Parameter("bool", "executed", 7)]
    public bool Executed { get; set; }
}