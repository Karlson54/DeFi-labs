using System.Numerics;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace DeFi.Models.Contracts;

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