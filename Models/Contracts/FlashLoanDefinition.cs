using System.Numerics;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace DeFi.Models.Contracts;

public class DexPoolDeployment : ContractDeploymentMessage
{
    public DexPoolDeployment() : base(string.Empty) { }

    [Parameter("address", "tokenA_", 1)]
    public string TokenA { get; set; } = string.Empty;

    [Parameter("address", "tokenB_", 2)]
    public string TokenB { get; set; } = string.Empty;
}

[Function("addLiquidity")]
public class PoolAddLiquidityFunction : FunctionMessage
{
    [Parameter("uint256", "amountA", 1)]
    public BigInteger AmountA { get; set; }

    [Parameter("uint256", "amountB", 2)]
    public BigInteger AmountB { get; set; }
}

public class SpotPriceOracleDeployment : ContractDeploymentMessage
{
    public SpotPriceOracleDeployment() : base(string.Empty) { }

    [Parameter("address", "pool_", 1)]
    public string Pool { get; set; } = string.Empty;
}

public class TrustedPriceOracleDeployment : ContractDeploymentMessage
{
    public TrustedPriceOracleDeployment() : base(string.Empty) { }

    [Parameter("uint256", "initialPrice_", 1)]
    public BigInteger InitialPrice { get; set; }
}

[Function("getPrice", "uint256")]
public class GetPriceFunction : FunctionMessage
{
}

public class OracleLenderDeployment : ContractDeploymentMessage
{
    public OracleLenderDeployment() : base(string.Empty) { }

    [Parameter("address", "collateralToken_", 1)]
    public string CollateralToken { get; set; } = string.Empty;

    [Parameter("address", "borrowToken_", 2)]
    public string BorrowToken { get; set; } = string.Empty;

    [Parameter("address", "oracle_", 3)]
    public string Oracle { get; set; } = string.Empty;
}

[Function("collateral", "uint256")]
public class LenderCollateralFunction : FunctionMessage
{
    [Parameter("address", "", 1)]
    public string User { get; set; } = string.Empty;
}

[Function("debt", "uint256")]
public class LenderDebtFunction : FunctionMessage
{
    [Parameter("address", "", 1)]
    public string User { get; set; } = string.Empty;
}

public class FlashLoanProviderDeployment : ContractDeploymentMessage
{
    public FlashLoanProviderDeployment() : base(string.Empty) { }

    [Parameter("address", "token_", 1)]
    public string Token { get; set; } = string.Empty;
}

[Function("flashFee", "uint256")]
public class FlashFeeFunction : FunctionMessage
{
    [Parameter("uint256", "amount", 1)]
    public BigInteger Amount { get; set; }
}

public class FlashLoanAttackerDeployment : ContractDeploymentMessage
{
    public FlashLoanAttackerDeployment() : base(string.Empty) { }

    [Parameter("address", "provider_", 1)]
    public string Provider { get; set; } = string.Empty;

    [Parameter("address", "pool_", 2)]
    public string Pool { get; set; } = string.Empty;

    [Parameter("address", "lender_", 3)]
    public string Lender { get; set; } = string.Empty;

    [Parameter("address", "tokenA_", 4)]
    public string TokenA { get; set; } = string.Empty;

    [Parameter("address", "tokenB_", 5)]
    public string TokenB { get; set; } = string.Empty;
}

[Function("attack")]
public class AttackFunction : FunctionMessage
{
    [Parameter("uint256", "loanAmount", 1)]
    public BigInteger LoanAmount { get; set; }
}

[Function("withdrawProfit")]
public class WithdrawProfitFunction : FunctionMessage
{
    [Parameter("address", "to", 1)]
    public string To { get; set; } = string.Empty;
}