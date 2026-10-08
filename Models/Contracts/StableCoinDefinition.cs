using System.Numerics;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace DeFi.Models.Contracts;

public class StableCoinDeployment : ContractDeploymentMessage
{
    public StableCoinDeployment() : base(string.Empty) { }

    public StableCoinDeployment(string byteCode) : base(byteCode) { }

    [Parameter("string", "name_", 1)]
    public string Name { get; set; } = string.Empty;

    [Parameter("string", "symbol_", 2)]
    public string Symbol { get; set; } = string.Empty;
}

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

[Function("balanceOf", "uint256")]
public class StableCoinBalanceOfFunction : FunctionMessage
{
    [Parameter("address", "account", 1)]
    public string Account { get; set; } = string.Empty;
}

[Function("approve", "bool")]
public class StableCoinApproveFunction : FunctionMessage
{
    [Parameter("address", "spender", 1)]
    public string Spender { get; set; } = string.Empty;

    [Parameter("uint256", "value", 2)]
    public BigInteger Amount { get; set; }
}

[Function("transfer", "bool")]
public class StableCoinTransferFunction : FunctionMessage
{
    [Parameter("address", "to", 1)]
    public string To { get; set; } = string.Empty;

    [Parameter("uint256", "value", 2)]
    public BigInteger Amount { get; set; }
}