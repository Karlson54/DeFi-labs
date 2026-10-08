using System.Globalization;
using System.Text;
using DeFi.Models;

namespace DeFi.Services;

public interface IReportRenderer
{
    string Render(ScenarioReport report);

    string Render(CrashReport report);
}

public sealed class ConsoleReportRenderer : IReportRenderer
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public string Render(ScenarioReport report)
    {
        var sb = new StringBuilder();
        var symbol = report.Stablecoin.Symbol;

        Header(sb, "МЕРЕЖА ТА АКАУНТИ");
        sb.AppendLine($"  Мережа................: {report.Network}");
        sb.AppendLine($"  Позичальник (власник).: {report.DeployerAddress}");
        sb.AppendLine($"  Ліквідатор (бот)......: {report.LiquidatorAddress}");
        sb.AppendLine($"  Оракул Chainlink......: {report.Engine.PriceFeedAddress}");
        sb.AppendLine($"  Ціна ETH/USD (oracle).: ${Num(report.EthUsdPrice)}");

        Header(sb, "КРОК 1. РОЗГОРТАННЯ СТЕЙБЛКОЇНА ТА КРЕДИТНОГО ЯДРА");
        sb.AppendLine($"  Стейблкоїн............: {report.Stablecoin.Name} ({symbol})");
        sb.AppendLine($"    Адреса..............: {report.Stablecoin.Address}");
        sb.AppendLine($"    Статус..............: {Status(report.Stablecoin.WasAlreadyDeployed)}");
        sb.AppendLine($"  StableEngine..........: {report.Engine.Address}");
        sb.AppendLine($"    Статус..............: {Status(report.Engine.WasAlreadyDeployed)}");
        sb.AppendLine($"  transferOwnership.....: {(report.Ownership.WasAlreadyTransferred ? "вже виконано раніше" : "виконано щойно")}");
        sb.AppendLine($"    Новий власник токена: {report.Ownership.NewOwner}");

        Header(sb, "КРОК 2. ВНЕСЕННЯ ЗАСТАВИ (depositCollateral)");
        if (report.Deposit.TransactionHash is null)
        {
            sb.AppendLine("  Пропущено.............: потрібна застава вже внесена");
        }
        else
        {
            sb.AppendLine($"  Внесено...............: {Num(report.Deposit.AmountEth)} ETH");
            sb.AppendLine($"  Транзакція............: {report.Deposit.TransactionHash} (gas: {report.Deposit.GasUsed})");
        }
        RenderPosition(sb, report.PositionAfterDeposit, symbol);

        Header(sb, "КРОК 3. ЕМІСІЯ СТЕЙБЛКОЇНІВ ДО HEALTH FACTOR ≈ ЦІЛЬОВОГО");
        if (report.Mint.TransactionHash is null)
        {
            sb.AppendLine("  Пропущено.............: борг уже не менший за цільовий");
        }
        else
        {
            sb.AppendLine($"  Випущено..............: {Num(report.Mint.Amount)} {symbol}");
            sb.AppendLine($"  Транзакція............: {report.Mint.TransactionHash} (gas: {report.Mint.GasUsed})");
        }
        RenderPosition(sb, report.PositionAfterMint, symbol);

        Header(sb, "КРОК 4. ПІДГОТОВКА ГАМАНЦЯ ЛІКВІДАТОРА");
        var f = report.Funding;
        sb.AppendLine($"  ETH на газ............: надіслано {Num(f.EthSent)}, баланс {Num(f.EthBalance)} ETH");
        if (f.EthTransactionHash is not null)
        {
            sb.AppendLine($"    Транзакція..........: {f.EthTransactionHash}");
        }
        sb.AppendLine($"  Стейблкоїни для викупу: надіслано {Num(f.StableSent)}, баланс {Num(f.StableBalance)} {symbol}");
        if (f.StableTransactionHash is not null)
        {
            sb.AppendLine($"    Транзакція..........: {f.StableTransactionHash}");
        }

        sb.AppendLine();
        sb.AppendLine("Стенд готовий. Далі, у двох окремих терміналах:");
        sb.AppendLine("  1) dotnet run -- --bot     (запустити бота-ліквідатора)");
        sb.AppendLine("  2) dotnet run -- --crash   (бекдор: штучна неплатоспроможність позичальника)");
        sb.AppendLine();

        return sb.ToString();
    }

    public string Render(CrashReport report)
    {
        var sb = new StringBuilder();

        Header(sb, "ШТУЧНА НЕПЛАТОСПРОМОЖНІСТЬ (бекдор simulateInsolvency — ЛИШЕ ДЛЯ ЛАБОРАТОРНОЇ)");
        sb.AppendLine($"  Мережа................: {report.Network}");
        sb.AppendLine($"  StableEngine..........: {report.EngineAddress}");
        sb.AppendLine($"  Позичальник...........: {report.OwnerAddress}");
        sb.AppendLine($"  Ціна ETH/USD (oracle).: ${Num(report.EthUsdPrice)} (реальна, не змінювалась)");

        sb.AppendLine();
        sb.AppendLine("  До втручання:");
        RenderPosition(sb, report.Before, "USD");

        sb.AppendLine();
        sb.AppendLine($"  Зменшено заставу в реєстрі: {Num(report.Reduce.ReducedEth)} ETH ({Num(report.Percent)}%)");
        sb.AppendLine($"  Транзакція............: {report.Reduce.TransactionHash} (gas: {report.Reduce.GasUsed})");

        sb.AppendLine();
        sb.AppendLine("  Після втручання:");
        RenderPosition(sb, report.After, "USD");

        sb.AppendLine();
        if (report.After.HealthFactor is { } hf && hf < 1m)
        {
            sb.AppendLine("  [OK] HF < 1: позиція неплатоспроможна — бот-ліквідатор має її підхопити.");
        }
        else
        {
            sb.AppendLine("  [УВАГА] HF все ще ≥ 1: збільште Lab6Settings:CrashCollateralPercent.");
        }
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
        wasAlreadyDeployed ? "перевикористано з файлу стану" : "розгорнуто щойно";

    private static void Header(StringBuilder sb, string title)
    {
        sb.AppendLine();
        sb.AppendLine(new string('=', 78));
        sb.AppendLine($"  {title}");
        sb.AppendLine(new string('=', 78));
    }

    private static string Num(decimal value) => value.ToString("0.########", Culture);
}