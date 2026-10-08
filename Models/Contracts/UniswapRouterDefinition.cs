using System.Numerics;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace DeFi.Models.Contracts;

/// <summary>Виклик addLiquidity у Uniswap V2 Router (створює пул A/B, якщо його ще немає).</summary>
[Function("addLiquidity")]
public class AddLiquidityFunction : FunctionMessage
{
    [Parameter("address", "tokenA", 1)]
    public string TokenA { get; set; } = string.Empty;

    [Parameter("address", "tokenB", 2)]
    public string TokenB { get; set; } = string.Empty;

    [Parameter("uint256", "amountADesired", 3)]
    public BigInteger AmountADesired { get; set; }

    [Parameter("uint256", "amountBDesired", 4)]
    public BigInteger AmountBDesired { get; set; }

    [Parameter("uint256", "amountAMin", 5)]
    public BigInteger AmountAMin { get; set; }

    [Parameter("uint256", "amountBMin", 6)]
    public BigInteger AmountBMin { get; set; }

    [Parameter("address", "to", 7)]
    public string To { get; set; } = string.Empty;

    [Parameter("uint256", "deadline", 8)]
    public BigInteger Deadline { get; set; }
}