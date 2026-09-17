using System.Globalization;
using System.Text;
using DeFi.Models;

namespace DeFi.Services;

public interface IReportRenderer
{
    string Render(SimulationReport report);
}

public sealed class ConsoleReportRenderer : IReportRenderer
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public string Render(SimulationReport report)
    {
        var sb = new StringBuilder();

        Header(sb, "МЕРЕЖА ТА АКАУНТ");
        sb.AppendLine($"  Мережа................: {report.Network}");
        sb.AppendLine($"  Акаунт................: {report.DeployerAddress}");
        sb.AppendLine($"  Баланс на газ.........: {Num(report.DeployerNativeBalance)} ETH");

        Header(sb, "КРОК 1. ЕМІСІЯ ЦИФРОВИХ АКТИВІВ (ERC-20)");
        RenderToken(sb, report.TokenA, "Токен A");
        RenderToken(sb, report.TokenB, "Токен B");

        Header(sb, "КРОК 2. РОЗГОРТАННЯ ПУЛУ ЛІКВІДНОСТІ");
        sb.AppendLine($"  Адреса пулу...........: {report.Pool.Address}");
        sb.AppendLine($"  Статус................: {(report.Pool.WasAlreadyDeployed ? "перевикористано з deployment-state.json" : "розгорнуто щойно")}");

        Header(sb, "КРОК 3. НАДАННЯ ЛІКВІДНОСТІ (КУРС 1:2)");

        if (report.Liquidity.Skipped)
        {
            sb.AppendLine("  Пропущено: у пулі вже є резерви від попереднього запуску.");
        }
        else
        {
            sb.AppendLine($"  Внесено...............: {Num(report.Liquidity.AmountA)} {report.TokenA.Symbol} + {Num(report.Liquidity.AmountB)} {report.TokenB.Symbol}");
            sb.AppendLine($"  Транзакція............: {report.Liquidity.TransactionHash}");
        }

        RenderPoolState(sb, "  Резерви після внесення", report.Liquidity.PoolState, report);

        Header(sb, "КРОК 4. ДИНАМІЧНА КОМІСІЯ (КОНТРОЛЬНЕ ЗАВДАННЯ)");
        sb.AppendLine("  Обсяг угоди           Частка резерву   Комісія     Вихід");

        foreach (var tier in report.FeeTiers)
        {
            var share = report.Liquidity.PoolState.ReserveA == 0
                ? 0
                : tier.AmountIn / report.Liquidity.PoolState.ReserveA * 100m;

            sb.AppendLine(
                $"  {Num(tier.AmountIn),-20}  {Num(share),-14}%  {FormatBps(tier.FeeBps),-10}  {Num(tier.AmountOut)} {report.TokenB.Symbol}");
        }

        Header(sb, "КРОК 5. ОБМІН ТОКЕНІВ");
        sb.AppendLine($"  Продано...............: {Num(report.Swap.AmountIn)} {report.TokenA.Symbol}");
        sb.AppendLine($"  Отримано..............: {Num(report.Swap.AmountOut)} {report.TokenB.Symbol}");
        sb.AppendLine($"  Котирування до угоди..: {Num(report.Swap.QuotedAmountOut)} {report.TokenB.Symbol}");
        sb.AppendLine($"  minAmountOut..........: {Num(report.Swap.MinAmountOut)} {report.TokenB.Symbol}");
        sb.AppendLine($"  Застосована комісія...: {FormatBps(report.Swap.FeeBps)}");
        sb.AppendLine($"  Спот-курс до угоди....: 1 {report.TokenA.Symbol} = {Num(report.Swap.StateBefore.PriceAInB)} {report.TokenB.Symbol}");
        sb.AppendLine($"  Ефективний курс.......: 1 {report.TokenA.Symbol} = {Num(report.Swap.EffectivePrice)} {report.TokenB.Symbol}");
        sb.AppendLine($"  Проковзування.........: {Num(report.Swap.SlippagePercent)} %");
        sb.AppendLine($"  Транзакція............: {report.Swap.TransactionHash} (gas: {report.Swap.GasUsed})");

        Header(sb, "КРОК 6. ЗМІНА КОНСТАНТИ k");
        sb.AppendLine($"  k до угоди............: {Num(report.Swap.StateBefore.ConstantK)}");
        sb.AppendLine($"  k після угоди.........: {Num(report.Swap.StateAfter.ConstantK)}");
        sb.AppendLine($"  Приріст k.............: {Num(report.Swap.ConstantKGrowthPercent)} %  (це і є дохід провайдерів ліквідності)");
        sb.AppendLine($"  Резерви після.........: {Num(report.Swap.StateAfter.ReserveA)} {report.TokenA.Symbol} / {Num(report.Swap.StateAfter.ReserveB)} {report.TokenB.Symbol}");

        Header(sb, "КРОК 7. КООПЕРАЦІЯ");

        if (report.PartnerTransfer.Skipped)
        {
            sb.AppendLine("  Пропущено: у appsettings.json не вказано SimulationSettings:PartnerAddress.");
        }
        else
        {
            sb.AppendLine($"  Надіслано.............: {Num(report.PartnerTransfer.Amount)} {report.PartnerTransfer.Symbol}");
            sb.AppendLine($"  Отримувач.............: {report.PartnerTransfer.PartnerAddress}");
            sb.AppendLine($"  Транзакція............: {report.PartnerTransfer.TransactionHash}");
        }

        sb.AppendLine();
        return sb.ToString();
    }

    private static void RenderToken(StringBuilder sb, TokenDeploymentResult token, string label)
    {
        sb.AppendLine($"  {label}...............: {token.Name} ({token.Symbol})");
        sb.AppendLine($"    Адреса..............: {token.Address}");
        sb.AppendLine($"    Емісія..............: {Num(token.TotalSupply)} {token.Symbol}");
        sb.AppendLine($"    Статус..............: {(token.WasAlreadyDeployed ? "перевикористано" : "розгорнуто щойно")}");
    }

    private static void RenderPoolState(StringBuilder sb, string label, PoolStateSnapshot state, SimulationReport report)
    {
        sb.AppendLine($"{label}: {Num(state.ReserveA)} {report.TokenA.Symbol} / {Num(state.ReserveB)} {report.TokenB.Symbol}");
        sb.AppendLine($"  Курс..................: 1 {report.TokenA.Symbol} = {Num(state.PriceAInB)} {report.TokenB.Symbol}");
        sb.AppendLine($"  Константа k...........: {Num(state.ConstantK)}");
    }

    private static void Header(StringBuilder sb, string title)
    {
        sb.AppendLine();
        sb.AppendLine(new string('=', 78));
        sb.AppendLine($"  {title}");
        sb.AppendLine(new string('=', 78));
    }

    private static string FormatBps(int bps) => $"{(bps / 100m).ToString("0.00", Culture)}%";

    private static string Num(decimal value) => value.ToString("0.########", Culture);
}
