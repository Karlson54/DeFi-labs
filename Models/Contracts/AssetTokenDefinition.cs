using System.Numerics;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace DeFi.Models.Contracts;

public class AssetTokenDeployment : ContractDeploymentMessage
{
    public AssetTokenDeployment() : base(string.Empty) { }

    public AssetTokenDeployment(string byteCode) : base(byteCode) { }

    [Parameter("string", "name_", 1)]
    public string Name { get; set; } = string.Empty;

    [Parameter("string", "symbol_", 2)]
    public string Symbol { get; set; } = string.Empty;

    [Parameter("uint256", "initialSupply_", 3)]
    public BigInteger InitialSupply { get; set; }
}

[Function("balanceOf", "uint256")]
public class BalanceOfFunction : FunctionMessage
{
    [Parameter("address", "account", 1)]
    public string Account { get; set; } = string.Empty;
}

[Function("totalSupply", "uint256")]
public class TotalSupplyFunction : FunctionMessage
{
}

[Function("decimals", "uint8")]
public class DecimalsFunction : FunctionMessage
{
}

[Function("transfer", "bool")]
public class TransferFunction : FunctionMessage
{
    [Parameter("address", "to", 1)]
    public string To { get; set; } = string.Empty;

    [Parameter("uint256", "amount", 2)]
    public BigInteger Amount { get; set; }
}

[Function("approve", "bool")]
public class ApproveFunction : FunctionMessage
{
    [Parameter("address", "spender", 1)]
    public string Spender { get; set; } = string.Empty;

    [Parameter("uint256", "amount", 2)]
    public BigInteger Amount { get; set; }
}

[Function("allowance", "uint256")]
public class AllowanceFunction : FunctionMessage
{
    [Parameter("address", "owner", 1)]
    public string Owner { get; set; } = string.Empty;

    [Parameter("address", "spender", 2)]
    public string Spender { get; set; } = string.Empty;
}
