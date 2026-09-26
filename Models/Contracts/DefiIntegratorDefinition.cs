using System.Numerics;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace Defi.Models.Contracts;

public class DefiIntegratorDeployment : ContractDeploymentMessage
{
    public DefiIntegratorDeployment() : base(string.Empty) { }

    public DefiIntegratorDeployment(string byteCode) : base(byteCode) { }

    [Parameter("address", "router_", 1)]
    public string Router { get; set; } = string.Empty;
}

[Function("provideLiquidity", "uint256")]
public class ProvideLiquidityFunction : FunctionMessage
{
    [Parameter("address", "tokenA", 1)]
    public string TokenA { get; set; } = string.Empty;

    [Parameter("address", "tokenB", 2)]
    public string TokenB { get; set; } = string.Empty;

    [Parameter("uint256", "amountADesired", 3)]
    public BigInteger AmountADesired { get; set; }

    [Parameter("uint256", "amountBDesired", 4)]
    public BigInteger AmountBDesired { get; set; }
}

[Function("swapTokens", "uint256")]
public class SwapTokensFunction : FunctionMessage
{
    [Parameter("address", "tokenIn", 1)]
    public string TokenIn { get; set; } = string.Empty;

    [Parameter("address", "tokenOut", 2)]
    public string TokenOut { get; set; } = string.Empty;

    [Parameter("uint256", "amountIn", 3)]
    public BigInteger AmountIn { get; set; }

    [Parameter("uint256", "amountOutMin", 4)]
    public BigInteger AmountOutMin { get; set; }
}