using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace DeFi.Models.Contracts;

public class CrossChainReceiverDeployment : ContractDeploymentMessage
{
    public CrossChainReceiverDeployment() : base(string.Empty) { }

    public CrossChainReceiverDeployment(string byteCode) : base(byteCode) { }

    [Parameter("address", "router_", 1)]
    public string Router { get; set; } = string.Empty;
}

[Function("lastMessageId", "bytes32")]
public class LastMessageIdFunction : FunctionMessage
{
}

[Function("lastSourceChainSelector", "uint64")]
public class LastSourceChainSelectorFunction : FunctionMessage
{
}

[Function("lastSender", "address")]
public class LastSenderFunction : FunctionMessage
{
}

[Function("lastText", "string")]
public class LastTextFunction : FunctionMessage
{
}