using System.Numerics;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace DeFi.Models.Contracts;

public class DexPoolDeployment : ContractDeploymentMessage
{
    public DexPoolDeployment() : base(string.Empty) { }
    public DexPoolDeployment(string byteCode) : base(byteCode) { }

    [Parameter("address", "tokenA_", 1)]
    public string TokenA { get; set; } = string.Empty;

    [Parameter("address", "tokenB_", 2)]
    public string TokenB { get; set; } = string.Empty;
}

[Function("reserveA", "uint256")]
public class ReserveAFunction : FunctionMessage
{
}

[Function("reserveB", "uint256")]
public class ReserveBFunction : FunctionMessage
{
}

[Function("addLiquidity")]
public class AddLiquidityFunction : FunctionMessage
{
    [Parameter("uint256", "amountA", 1)]
    public BigInteger AmountA { get; set; }

    [Parameter("uint256", "amountB", 2)]
    public BigInteger AmountB { get; set; }
}

[Function("swapAForB", "uint256")]
public class SwapAForBFunction : FunctionMessage
{
    [Parameter("uint256", "amountIn", 1)]
    public BigInteger AmountIn { get; set; }

    [Parameter("uint256", "minAmountOut", 2)]
    public BigInteger MinAmountOut { get; set; }
}

[Function("swapBForA", "uint256")]
public class SwapBForAFunction : FunctionMessage
{
    [Parameter("uint256", "amountIn", 1)]
    public BigInteger AmountIn { get; set; }

    [Parameter("uint256", "minAmountOut", 2)]
    public BigInteger MinAmountOut { get; set; }
}

[Function("getAmountOut", "uint256")]
public class GetAmountOutFunction : FunctionMessage
{
    [Parameter("uint256", "amountIn", 1)]
    public BigInteger AmountIn { get; set; }

    [Parameter("uint256", "reserveIn", 2)]
    public BigInteger ReserveIn { get; set; }

    [Parameter("uint256", "reserveOut", 3)]
    public BigInteger ReserveOut { get; set; }
}

[Function("getFeeBps", "uint256")]
public class GetFeeBpsFunction : FunctionMessage
{
    [Parameter("uint256", "amountIn", 1)]
    public BigInteger AmountIn { get; set; }

    [Parameter("uint256", "reserveIn", 2)]
    public BigInteger ReserveIn { get; set; }
}

[Event("Swap")]
public class SwapEventDto : IEventDTO
{
    [Parameter("address", "trader", 1, true)]
    public string Trader { get; set; } = string.Empty;

    [Parameter("address", "tokenIn", 2, true)]
    public string TokenIn { get; set; } = string.Empty;

    [Parameter("uint256", "amountIn", 3, false)]
    public BigInteger AmountIn { get; set; }

    [Parameter("uint256", "amountOut", 4, false)]
    public BigInteger AmountOut { get; set; }

    [Parameter("uint256", "feeBps", 5, false)]
    public BigInteger FeeBps { get; set; }
}
