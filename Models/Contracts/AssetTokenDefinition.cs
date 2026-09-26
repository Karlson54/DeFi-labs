using System.Numerics;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace Defi.Models.Contracts;

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

[Function("totalSupply", "uint256")]
public class TotalSupplyFunction : FunctionMessage
{
}

[Function("balanceOf", "uint256")]
public class BalanceOfFunction : FunctionMessage
{
    [Parameter("address", "account", 1)]
    public string Account { get; set; } = string.Empty;
}

[Function("approve", "bool")]
public class ApproveFunction : FunctionMessage
{
    [Parameter("address", "spender", 1)]
    public string Spender { get; set; } = string.Empty;

    [Parameter("uint256", "amount", 2)]
    public BigInteger Amount { get; set; }
}