using System.Numerics;
using DeFi.Models;
using Microsoft.Extensions.Options;
using Nethereum.Web3;

namespace DeFi.Services;

public interface IAttackRunner
{
    Task<AttackReport> RunAsync(CancellationToken cancellationToken = default);
}

public sealed class AttackRunner : IAttackRunner
{
    private const int Decimals = 18;

    private readonly IWeb3Factory _web3Factory;
    private readonly ITokenService _tokenService;
    private readonly IFlashLoanService _flash;
    private readonly Lab9Settings _settings;

    public AttackRunner(
        IWeb3Factory web3Factory,
        ITokenService tokenService,
        IFlashLoanService flash,
        IOptions<Lab9Settings> settingsOptions)
    {
        _web3Factory = web3Factory;
        _tokenService = tokenService;
        _flash = flash;
        _settings = settingsOptions.Value;
    }

    public async Task<AttackReport> RunAsync(CancellationToken cancellationToken = default)
    {
        ValidateSettings();

        var s = _settings.Attack;
        var user = _web3Factory.AccountAddress;

        var tokenA = await _tokenService.DeployAsync(_settings.TokenA, cancellationToken);
        var tokenB = await _tokenService.DeployAsync(_settings.TokenB, cancellationToken);

        var pool = await _flash.DeployPoolAsync(tokenA.Address, tokenB.Address, cancellationToken);
        await _tokenService.ApproveAsync(tokenA.Address, pool.Address, s.PoolLiquidityA, cancellationToken);
        await _tokenService.ApproveAsync(tokenB.Address, pool.Address, s.PoolLiquidityB, cancellationToken);
        var liquidityTx = await _flash.AddLiquidityAsync(pool.Address, s.PoolLiquidityA, s.PoolLiquidityB, cancellationToken);

        var honestPrice = s.PoolLiquidityB / s.PoolLiquidityA;
        var spotOracle = await _flash.DeploySpotOracleAsync(pool.Address, cancellationToken);
        var trustedOracle = await _flash.DeployTrustedOracleAsync(honestPrice, cancellationToken);

        var vulnerableLender = await _flash.DeployLenderAsync(tokenA.Address, tokenB.Address, spotOracle.Address, cancellationToken);
        var safeLender = await _flash.DeployLenderAsync(tokenA.Address, tokenB.Address, trustedOracle.Address, cancellationToken);
        await _tokenService.TransferAsync(tokenB.Address, vulnerableLender.Address, s.LenderLiquidityB, cancellationToken);
        await _tokenService.TransferAsync(tokenB.Address, safeLender.Address, s.LenderLiquidityB, cancellationToken);

        var provider = await _flash.DeployProviderAsync(tokenB.Address, cancellationToken);
        await _tokenService.TransferAsync(tokenB.Address, provider.Address, s.ProviderLiquidityB, cancellationToken);

        var attackerV = await _flash.DeployAttackerAsync(
            provider.Address, pool.Address, vulnerableLender.Address, tokenA.Address, tokenB.Address, cancellationToken);
        var attackerS = await _flash.DeployAttackerAsync(
            provider.Address, pool.Address, safeLender.Address, tokenA.Address, tokenB.Address, cancellationToken);

        var spotBefore = await _flash.GetPriceAsync(spotOracle.Address, cancellationToken);
        var fee = await _flash.GetFlashFeeAsync(provider.Address, s.FlashLoanAmount, cancellationToken);

        var vulnerable = await ExecuteAttackAsync(attackerV.Address, vulnerableLender.Address, tokenB.Address, user, cancellationToken);
        if (vulnerable.Reverted)
        {
            throw new InvalidOperationException(
                $"Атака на вразливого кредитора неочікувано відкотилась: {vulnerable.RevertReason}. " +
                "Перевірте ліквідність кредитора й провайдера та налаштування Lab9Settings:Attack.");
        }

        var spotAfter = await _flash.GetPriceAsync(spotOracle.Address, cancellationToken);

        var protectedOutcome = await ExecuteAttackAsync(attackerS.Address, safeLender.Address, tokenB.Address, user, cancellationToken);
        if (!protectedOutcome.Reverted)
        {
            throw new InvalidOperationException(
                "КРИТИЧНО: атака на кредитора із захищеним оракулом пройшла успішно. " +
                "Перевірте, що SafeLender підключено до TrustedPriceOracle, а не до SpotPriceOracle.");
        }

        return new AttackReport(
            Network: $"chainId {_web3Factory.ChainId}",
            DeployerAddress: user,
            SymbolA: _settings.TokenA.Symbol,
            SymbolB: _settings.TokenB.Symbol,
            TokenA: new ContractRef(_settings.TokenA.Name, tokenA.Address, false, tokenA.TransactionHash),
            TokenB: new ContractRef(_settings.TokenB.Name, tokenB.Address, false, tokenB.TransactionHash),
            Pool: new ContractRef("DexPool (A/B)", pool.Address, false, pool.TransactionHash),
            Provider: new ContractRef("FlashLoanProvider", provider.Address, false, provider.TransactionHash),
            SpotOracle: new ContractRef("SpotPriceOracle", spotOracle.Address, false, spotOracle.TransactionHash),
            TrustedOracle: new ContractRef("TrustedPriceOracle", trustedOracle.Address, false, trustedOracle.TransactionHash),
            VulnerableLender: new ContractRef("OracleLender (спот-ціна)", vulnerableLender.Address, false, vulnerableLender.TransactionHash),
            SafeLender: new ContractRef("OracleLender (фід)", safeLender.Address, false, safeLender.TransactionHash),
            VulnerableAttacker: new ContractRef("FlashLoanAttacker #1", attackerV.Address, false, attackerV.TransactionHash),
            SafeAttacker: new ContractRef("FlashLoanAttacker #2", attackerS.Address, false, attackerS.TransactionHash),
            SpotPriceBefore: spotBefore,
            SpotPriceAfter: spotAfter,
            TrustedPrice: honestPrice,
            LoanAmount: s.FlashLoanAmount,
            FlashFee: fee,
            Vulnerable: vulnerable,
            Protected: protectedOutcome);
    }

    private async Task<AttackOutcome> ExecuteAttackAsync(
        string attacker, string lender, string tokenB, string owner, CancellationToken cancellationToken)
    {
        var lenderBeforeWei = await _tokenService.BalanceOfAsync(tokenB, lender, cancellationToken);

        var attempt = await _flash.TryAttackAsync(attacker, _settings.Attack.FlashLoanAmount, cancellationToken);

        var lenderAfterWei = await _tokenService.BalanceOfAsync(tokenB, lender, cancellationToken);
        var profitWei = await _tokenService.BalanceOfAsync(tokenB, attacker, cancellationToken);
        var debt = await _flash.GetDebtAsync(lender, attacker, cancellationToken);
        var collateral = await _flash.GetCollateralAsync(lender, attacker, cancellationToken);

        string? withdrawTx = null;
        if (!attempt.Reverted && profitWei > BigInteger.Zero)
        {
            withdrawTx = await _flash.WithdrawProfitAsync(attacker, owner, cancellationToken);
        }

        return new AttackOutcome(
            attempt.Reverted,
            attempt.RevertReason,
            attempt.TransactionHash,
            attempt.GasUsed,
            Web3.Convert.FromWei(profitWei, Decimals),
            Web3.Convert.FromWei(lenderBeforeWei, Decimals),
            Web3.Convert.FromWei(lenderAfterWei, Decimals),
            debt,
            collateral,
            withdrawTx);
    }

    private void ValidateSettings()
    {
        var s = _settings.Attack;

        if (s.PoolLiquidityA <= 0 || s.PoolLiquidityB <= 0 || s.LenderLiquidityB <= 0 ||
            s.ProviderLiquidityB <= 0 || s.FlashLoanAmount <= 0)
        {
            throw new InvalidOperationException("У Lab9Settings:Attack усі суми мають бути додатними.");
        }

        if (s.FlashLoanAmount > s.ProviderLiquidityB)
        {
            throw new InvalidOperationException(
                "Lab9Settings:Attack:FlashLoanAmount перевищує ProviderLiquidityB: кредитор не зможе видати позику.");
        }
    }
}