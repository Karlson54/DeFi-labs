using System.Numerics;

namespace DeFi.Models;

public sealed record ReceiverDeploymentResult(
    string Address,
    string RouterAddress,
    bool WasAlreadyDeployed,
    string? TransactionHash);

public sealed record MessengerDeploymentResult(
    string Address,
    string RouterAddress,
    string LinkTokenAddress,
    bool WasAlreadyDeployed,
    string? TransactionHash);

public sealed record LinkFundingResult(
    string LinkTokenAddress,
    decimal Sent,
    string? TransactionHash,
    decimal MessengerBalance,
    decimal DeployerBalance);

public sealed record SendResult(
    string MessageId,
    string Text,
    decimal FeeLink,
    string TransactionHash,
    BigInteger BlockNumber,
    BigInteger GasUsed);

public sealed record ReceivedMessage(
    string MessageId,
    ulong SourceChainSelector,
    string Sender,
    string Text);

public sealed record ScenarioReport(
    string SourceNetwork,
    string DestinationNetwork,
    string DeployerAddress,
    ulong DestinationChainSelector,
    ReceiverDeploymentResult Receiver,
    MessengerDeploymentResult Messenger,
    LinkFundingResult Funding,
    decimal EstimatedFeeLink,
    SendResult Send,
    string ExplorerUrl);

public sealed record DeliveryReport(
    string DestinationNetwork,
    string ReceiverAddress,
    string MessageId,
    bool Delivered,
    TimeSpan Elapsed,
    ReceivedMessage? Received,
    string ExplorerUrl);