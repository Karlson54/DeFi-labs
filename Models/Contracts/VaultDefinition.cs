using System.Numerics;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace DeFi.Models.Contracts;

public class AutoCompoundVaultDeployment : ContractDeploymentMessage
{
    public AutoCompoundVaultDeployment() : base(string.Empty) { }

    public AutoCompoundVaultDeployment(string byteCode) : base(byteCode) { }

    [Parameter("address", "asset_", 1)]
    public string Asset { get; set; } = string.Empty;

    [Parameter("address", "rewardToken_", 2)]
    public string RewardToken { get; set; } = string.Empty;

    [Parameter("address", "router_", 3)]
    public string Router { get; set; } = string.Empty;
}

[Function("deposit", "uint256")]
public class VaultDepositFunction : FunctionMessage
{
    [Parameter("uint256", "assets", 1)]
    public BigInteger Assets { get; set; }
}

[Function("withdraw", "uint256")]
public class VaultWithdrawFunction : FunctionMessage
{
    [Parameter("uint256", "shares", 1)]
    public BigInteger Shares { get; set; }
}

[Function("compound", "uint256")]
public class CompoundFunction : FunctionMessage
{
}

[Function("totalAssets", "uint256")]
public class TotalAssetsFunction : FunctionMessage
{
}

[Function("totalSupply", "uint256")]
public class VaultTotalSupplyFunction : FunctionMessage
{
}

[Function("balanceOf", "uint256")]
public class VaultBalanceOfFunction : FunctionMessage
{
    [Parameter("address", "account", 1)]
    public string Account { get; set; } = string.Empty;
}

[Function("convertToAssets", "uint256")]
public class ConvertToAssetsFunction : FunctionMessage
{
    [Parameter("uint256", "shares", 1)]
    public BigInteger Shares { get; set; }
}

[Function("convertToShares", "uint256")]
public class ConvertToSharesFunction : FunctionMessage
{
    [Parameter("uint256", "assets", 1)]
    public BigInteger Assets { get; set; }
}