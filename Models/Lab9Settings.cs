namespace DeFi.Models;

public sealed class Lab9Settings
{
    public TokenSettings TokenA { get; set; } = new();
    public TokenSettings TokenB { get; set; } = new();
    public DaoSettings Dao { get; set; } = new();
    public AttackSettings Attack { get; set; } = new();
}

public sealed class TokenSettings
{
    public string Name { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public decimal InitialSupply { get; set; } = 1_000_000m;
}

public sealed class DaoSettings
{
    public int InitialFee { get; set; } = 3;
    public int NewFee { get; set; } = 10;
    public int VotingWaitBufferSeconds { get; set; } = 5;
    public string ProposalDescription { get; set; } = "Proposal #1: встановити комісію MockProtocol = 10";
}

public sealed class AttackSettings
{
    public decimal PoolLiquidityA { get; set; } = 1000m;
    public decimal PoolLiquidityB { get; set; } = 2000m;
    public decimal LenderLiquidityB { get; set; } = 50_000m;
    public decimal ProviderLiquidityB { get; set; } = 20_000m;
    public decimal FlashLoanAmount { get; set; } = 10_000m;
}