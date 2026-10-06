using System.Numerics;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace DeFi.Models.Contracts;

public class StableEngineDeployment : ContractDeploymentMessage
{
    public StableEngineDeployment() : base(string.Empty) { }

    public StableEngineDeployment(string byteCode) : base(byteCode) { }

    [Parameter("address", "stablecoin_", 1)]
    public string Stablecoin { get; set; } = string.Empty;

    [Parameter("uint256", "initialPrice_", 2)]
    public BigInteger InitialPrice { get; set; }
}

[Function("depositCollateral")]
public class DepositCollateralFunction : FunctionMessage
{
}

[Function("mintStablecoin")]
public class MintStablecoinFunction : FunctionMessage
{
    [Parameter("uint256", "amount", 1)]
    public BigInteger Amount { get; set; }
}

[Function("burnStablecoin")]
public class BurnStablecoinFunction : FunctionMessage
{
    [Parameter("uint256", "amount", 1)]
    public BigInteger Amount { get; set; }
}

[Function("withdrawCollateral")]
public class WithdrawCollateralFunction : FunctionMessage
{
    [Parameter("uint256", "amount", 1)]
    public BigInteger Amount { get; set; }
}

[Function("setMockEthUsdPrice")]
public class SetMockEthUsdPriceFunction : FunctionMessage
{
    [Parameter("uint256", "newPrice", 1)]
    public BigInteger NewPrice { get; set; }
}

[Function("getHealthFactor", "uint256")]
public class GetHealthFactorFunction : FunctionMessage
{
    [Parameter("address", "user", 1)]
    public string User { get; set; } = string.Empty;
}

[Function("getCollateralValueInUsd", "uint256")]
public class GetCollateralValueInUsdFunction : FunctionMessage
{
    [Parameter("address", "user", 1)]
    public string User { get; set; } = string.Empty;
}

[Function("collateralDeposited", "uint256")]
public class CollateralDepositedFunction : FunctionMessage
{
    [Parameter("address", "", 1)]
    public string User { get; set; } = string.Empty;
}

[Function("stablecoinMinted", "uint256")]
public class StablecoinMintedFunction : FunctionMessage
{
    [Parameter("address", "", 1)]
    public string User { get; set; } = string.Empty;
}

[Function("mockEthUsdPrice", "uint256")]
public class MockEthUsdPriceFunction : FunctionMessage
{
}

[Function("COLLATERALIZATION_RATIO", "uint256")]
public class CollateralizationRatioFunction : FunctionMessage
{
}