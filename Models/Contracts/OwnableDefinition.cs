using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace DeFi.Models.Contracts;

[Function("owner", "address")]
public class OwnerFunction : FunctionMessage
{
}

[Function("transferOwnership")]
public class TransferOwnershipFunction : FunctionMessage
{
    [Parameter("address", "newOwner", 1)]
    public string NewOwner { get; set; } = string.Empty;
}