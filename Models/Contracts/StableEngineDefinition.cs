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

    [Parameter("address", "priceFeed_", 2)]
    public string PriceFeed { get; set; } = string.Empty;
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

[Function("liquidate")]
public class LiquidateFunction : FunctionMessage
{
    [Parameter("address", "user", 1)]
    public string User { get; set; } = string.Empty;
}

// Лабораторний бекдор: примусове зменшення застави в реєстрі.
[Function("simulateInsolvency")]
public class SimulateInsolvencyFunction : FunctionMessage
{
    [Parameter("address", "user", 1)]
    public string User { get; set; } = string.Empty;

    [Parameter("uint256", "amount", 2)]
    public BigInteger Amount { get; set; }
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

[Function("getEthUsdPrice", "uint256")]
public class GetEthUsdPriceFunction : FunctionMessage
{
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

[Function("COLLATERALIZATION_RATIO", "uint256")]
public class CollateralizationRatioFunction : FunctionMessage
{
}