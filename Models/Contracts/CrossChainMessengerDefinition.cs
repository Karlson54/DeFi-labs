using System.Numerics;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace DeFi.Models.Contracts;

public class CrossChainMessengerDeployment : ContractDeploymentMessage
{
    public CrossChainMessengerDeployment() : base(string.Empty) { }

    public CrossChainMessengerDeployment(string byteCode) : base(byteCode) { }

    [Parameter("address", "router_", 1)]
    public string Router { get; set; } = string.Empty;

    [Parameter("address", "link_", 2)]
    public string Link { get; set; } = string.Empty;
}

[Function("sendMessage", "bytes32")]
public class SendMessageFunction : FunctionMessage
{
    [Parameter("uint64", "destinationChainSelector", 1)]
    public ulong DestinationChainSelector { get; set; }

    [Parameter("address", "receiver", 2)]
    public string Receiver { get; set; } = string.Empty;

    [Parameter("string", "text", 3)]
    public string Text { get; set; } = string.Empty;
}

[Function("getFee", "uint256")]
public class GetFeeFunction : FunctionMessage
{
    [Parameter("uint64", "destinationChainSelector", 1)]
    public ulong DestinationChainSelector { get; set; }

    [Parameter("address", "receiver", 2)]
    public string Receiver { get; set; } = string.Empty;

    [Parameter("string", "text", 3)]
    public string Text { get; set; } = string.Empty;
}

[Event("MessageSent")]
public class MessageSentEventDto : IEventDTO
{
    [Parameter("bytes32", "messageId", 1, true)]
    public byte[] MessageId { get; set; } = Array.Empty<byte>();

    [Parameter("uint64", "destinationChainSelector", 2, true)]
    public ulong DestinationChainSelector { get; set; }

    [Parameter("address", "receiver", 3, false)]
    public string Receiver { get; set; } = string.Empty;

    [Parameter("string", "text", 4, false)]
    public string Text { get; set; } = string.Empty;

    [Parameter("address", "feeToken", 5, false)]
    public string FeeToken { get; set; } = string.Empty;

    [Parameter("uint256", "fees", 6, false)]
    public BigInteger Fees { get; set; }
}