using System.Globalization;
using System.Text;
using DeFi.Models;

namespace DeFi.Services;

public interface IReportRenderer
{
    string Render(ScenarioReport report);
}

public sealed class ConsoleReportRenderer : IReportRenderer
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public string Render(ScenarioReport report)
    {
        var sb = new StringBuilder();
        var symbol = report.Stablecoin.Symbol;

        Header(sb, "МЕРЕЖА ТА АКАУНТ");
        sb.AppendLine($"  Мережа................: {report.Network}");
        sb.AppendLine($"  Акаунт (позичальник)..: {report.DeployerAddress}");
        sb.AppendLine($"  Мок-ціна ETH/USD......: ${Num(report.EthUsdPrice)}");

        Header(sb, "КРОК 1. РОЗГОРТАННЯ СТЕЙБЛКОЇНА ТА КРЕДИТНОГО ЯДРА");
        sb.AppendLine($"  Стейблкоїн............: {report.Stablecoin.Name} ({symbol})");
        sb.AppendLine($"    Адреса..............: {report.Stablecoin.Address}");
        sb.AppendLine($"    Статус..............: {Status(report.Stablecoin.WasAlreadyDeployed)}");
        sb.AppendLine($"  StableEngine..........: {report.Engine.Address}");
        sb.AppendLine($"    Статус..............: {Status(report.Engine.WasAlreadyDeployed)}");
        sb.AppendLine($"  transferOwnership.....: {(report.Ownership.WasAlreadyTransferred ? "вже виконано раніше" : "виконано щойно")}");
        sb.AppendLine($"    Новий власник токена: {report.Ownership.NewOwner}");

        Header(sb, "КРОК 2. ВНЕСЕННЯ ЗАСТАВИ (depositCollateral)");
        sb.AppendLine($"  Внесено...............: {Num(report.Deposit.AmountEth)} ETH");
        sb.AppendLine($"  Транзакція............: {report.Deposit.TransactionHash} (gas: {report.Deposit.GasUsed})");
        RenderPosition(sb, report.PositionAfterDeposit, symbol);

        Header(sb, "КРОК 3. ЕМІСІЯ МАКСИМАЛЬНОЇ СУМИ СТЕЙБЛКОЇНІВ (mintStablecoin)");
        sb.AppendLine($"  Випущено..............: {Num(report.Mint.Amount)} {symbol}");
        sb.AppendLine($"  Транзакція............: {report.Mint.TransactionHash} (gas: {report.Mint.GasUsed})");
        RenderPosition(sb, report.PositionAfterMint, symbol);

        Header(sb, "КРОК 4. СПРОБА ЗНЯТИ ЗАСТАВУ (withdrawCollateral) — ОЧІКУЄТЬСЯ REVERT");
        sb.AppendLine($"  Спроба зняти..........: {Num(report.BlockedWithdraw.AmountEth)} ETH");
        sb.AppendLine($"  Причина відкату.......: {report.BlockedWithdraw.RevertReason}");
        sb.AppendLine($"  Заставу не зменшено...: {Num(report.PositionAfterBlocked.CollateralEth)} ETH");
        sb.AppendLine();
        sb.AppendLine("  [OK] Захист спрацював успішно: транзакцію відхилено, бо Health Factor впав би нижче 1.");

        Header(sb, "КРОК 5. ПОГАШЕННЯ БОРГУ (burnStablecoin) І ПОВТОРНЕ ЗНЯТТЯ");
        sb.AppendLine($"  Спалено...............: {Num(report.Burn.Amount)} {symbol}");
        sb.AppendLine($"  Транзакція............: {report.Burn.TransactionHash} (gas: {report.Burn.GasUsed})");
        sb.AppendLine($"  Знято застави.........: {Num(report.WithdrawAfterBurn.AmountEth)} ETH");
        sb.AppendLine($"  Транзакція............: {report.WithdrawAfterBurn.TransactionHash}");
        RenderPosition(sb, report.FinalPosition, symbol);

        sb.AppendLine();
        sb.AppendLine("Over-collateralization підтверджено: протокол не дозволяє ні випустити");
        sb.AppendLine("незабезпечений борг, ні вивести заставу, що залишає позицію неплатоспроможною.");
        sb.AppendLine();

        return sb.ToString();
    }

    private static void RenderPosition(StringBuilder sb, PositionSnapshot position, string symbol)
    {
        sb.AppendLine("  Позиція:");
        sb.AppendLine($"    Застава.............: {Num(position.CollateralEth)} ETH (${Num(position.CollateralUsd)})");
        sb.AppendLine($"    Борг................: {Num(position.DebtUsd)} {symbol}");
        sb.AppendLine($"    Health Factor.......: {HealthFactor(position.HealthFactor)}");
    }

    private static string HealthFactor(decimal? value) =>
        value is null ? "∞ (боргу немає)" : value.Value.ToString("0.####", Culture);

    private static string Status(bool wasAlreadyDeployed) =>
        wasAlreadyDeployed ? "перевикористано з deployment-state.json" : "розгорнуто щойно";

    private static void Header(StringBuilder sb, string title)
    {
        sb.AppendLine();
        sb.AppendLine(new string('=', 78));
        sb.AppendLine($"  {title}");
        sb.AppendLine(new string('=', 78));
    }

    private static string Num(decimal value) => value.ToString("0.########", Culture);
}