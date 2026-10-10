using System.Numerics;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace DeFi.Models.Contracts;

[Function("balanceOf", "uint256")]
public class LinkBalanceOfFunction : FunctionMessage
{
    [Parameter("address", "account", 1)]
    public string Account { get; set; } = string.Empty;
}

[Function("transfer", "bool")]
public class LinkTransferFunction : FunctionMessage
{
    [Parameter("address", "to", 1)]
    public string To { get; set; } = string.Empty;

    [Parameter("uint256", "value", 2)]
    public BigInteger Amount { get; set; }
}