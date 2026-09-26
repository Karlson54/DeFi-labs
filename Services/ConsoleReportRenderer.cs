using System.Globalization;
using System.Text;
using Defi.Models;

namespace Defi.Services;

public interface IReportRenderer
{
    string Render(IntegrationReport report);
}

public sealed class ConsoleReportRenderer : IReportRenderer
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public string Render(IntegrationReport report)
    {
        var sb = new StringBuilder();

        Header(sb, "МЕРЕЖА ТА АКАУНТ");
        sb.AppendLine($"  Мережа................: {report.Network}");
        sb.AppendLine($"  Акаунт................: {report.DeployerAddress}");
        sb.AppendLine($"  Router (зовнішній DEX): {report.RouterAddress}");

        Header(sb, "КРОК 1. ЕМІСІЯ ВЛАСНИХ ERC-20 ТОКЕНІВ");
        RenderToken(sb, report.TokenA, "Токен A");
        RenderToken(sb, report.TokenB, "Токен B");

        Header(sb, "КРОК 2. РОЗГОРТАННЯ КОНТРАКТУ-ІНТЕГРАТОРА (DefiIntegrator)");
        sb.AppendLine($"  Адреса інтегратора....: {report.Integrator.Address}");
        sb.AppendLine($"  Статус................: {(report.Integrator.WasAlreadyDeployed ? "перевикористано з deployment-state.json" : "розгорнуто щойно")}");
        if (report.Integrator.TransactionHash is not null)
        {
            sb.AppendLine($"  Транзакція деплою.....: {report.Integrator.TransactionHash}");
        }

        Header(sb, "КРОК 3. ДЕЛЕГОВАНЕ ДОДАВАННЯ ЛІКВІДНОСТІ (через Router стороннього протоколу)");
        sb.AppendLine($"  Внесено...............: {Num(report.Liquidity.AmountA)} {report.TokenA.Symbol} + {Num(report.Liquidity.AmountB)} {report.TokenB.Symbol}");
        sb.AppendLine($"  Транзакція............: {report.Liquidity.TransactionHash} (gas: {report.Liquidity.GasUsed})");

        Header(sb, "КРОК 4. МІЖКОНТРАКТНИЙ ОБМІН (swapTokens -> Router.swapExactTokensForTokens)");
        sb.AppendLine($"  Продано...............: {Num(report.Swap.AmountIn)} {report.TokenA.Symbol}");
        sb.AppendLine($"  amountOutMin..........: {Num(report.Swap.AmountOutMin)} {report.TokenB.Symbol}");
        sb.AppendLine($"  Транзакція............: {report.Swap.TransactionHash} (gas: {report.Swap.GasUsed})");

        sb.AppendLine();
        sb.AppendLine("Композитність підтверджена: DefiIntegrator викликав чужий Router-контракт,");
        sb.AppendLine("не маючи власної реалізації математики AMM-пулу.");
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

    private static void Header(StringBuilder sb, string title)
    {
        sb.AppendLine();
        sb.AppendLine(new string('=', 78));
        sb.AppendLine($"  {title}");
        sb.AppendLine(new string('=', 78));
    }

    private static string Num(decimal value) => value.ToString("0.########", Culture);
}