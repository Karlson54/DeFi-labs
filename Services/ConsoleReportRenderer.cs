using System.Globalization;
using System.Text;
using DeFi.Models;

namespace DeFi.Services;

public interface IReportRenderer
{
    string Render(ScenarioReport report);

    string Render(StandReport report);

    string Render(DonationReport report);
}

public sealed class ConsoleReportRenderer : IReportRenderer
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public string Render(ScenarioReport report)
    {
        var sb = new StringBuilder();
        var assetSymbol = report.TokenA.Symbol;
        var rewardSymbol = report.TokenB.Symbol;

        RenderInfrastructure(sb, report.Network, report.UserAddress, report.RouterAddress,
            report.TokenA, report.TokenB, report.Pool, report.Vault);

        Header(sb, "КРОК 3. ДЕПОЗИТ У СХОВИЩЕ (deposit)");
        sb.AppendLine($"  Внесено...............: {Num(report.Deposit.Assets)} {assetSymbol}");
        sb.AppendLine($"  Отримано акцій........: {Num(report.Deposit.Shares)} {report.Vault.Address[..8]}… (shares)");
        sb.AppendLine($"  Транзакція............: {report.Deposit.TransactionHash} (gas: {report.Deposit.GasUsed})");
        RenderPosition(sb, report.PositionAfterDeposit, assetSymbol);

        Header(sb, "КРОК 4. ІМІТАЦІЯ ВИНАГОРОДИ (прямий ERC-20 переказ на сховище)");
        sb.AppendLine($"  Надіслано.............: {Num(report.Reward.Amount)} {rewardSymbol}");
        sb.AppendLine($"  Баланс винагороди.....: {Num(report.Reward.RewardBalanceInVault)} {rewardSymbol}");
        sb.AppendLine($"  Транзакція............: {report.Reward.TransactionHash}");
        RenderPosition(sb, report.PositionAfterReward, assetSymbol);
        sb.AppendLine();
        sb.AppendLine("  Зверніть увагу: ціна акції не змінилась — токени винагороди ще не входять у totalAssets().");

        Header(sb, "КРОК 5. РЕІНВЕСТУВАННЯ (compound -> Router.swapExactTokensForTokens)");
        sb.AppendLine($"  Продано винагороди....: {Num(report.Compound.RewardSold)} {rewardSymbol}");
        sb.AppendLine($"  Отримано активу.......: {Num(report.Compound.AssetsReceived)} {assetSymbol}");
        sb.AppendLine($"  totalAssets...........: {Num(report.Compound.TotalAssetsBefore)} -> {Num(report.Compound.TotalAssetsAfter)} {assetSymbol}");
        sb.AppendLine($"  Транзакція............: {report.Compound.TransactionHash} (gas: {report.Compound.GasUsed})");
        RenderPosition(sb, report.PositionAfterCompound, assetSymbol);

        Header(sb, "КРОК 6. ПЕРЕВІРКА ВАРТОСТІ АКЦІЙ (convertToAssets) ТА ЗНЯТТЯ (withdraw)");
        sb.AppendLine($"  Вартість акцій ДО compound.: {Num(report.PositionAfterReward.AssetsValue)} {assetSymbol}");
        sb.AppendLine($"  Вартість акцій ПІСЛЯ.......: {Num(report.PositionAfterCompound.AssetsValue)} {assetSymbol}");
        sb.AppendLine($"  Спалено акцій.........: {Num(report.Withdraw.Shares)}");
        sb.AppendLine($"  Отримано активу.......: {Num(report.Withdraw.Assets)} {assetSymbol}");
        sb.AppendLine($"  Транзакція............: {report.Withdraw.TransactionHash} (gas: {report.Withdraw.GasUsed})");

        var profit = report.Withdraw.Assets - report.Deposit.Assets;
        var percent = report.Deposit.Assets == 0m ? 0m : profit / report.Deposit.Assets * 100m;

        sb.AppendLine();
        sb.AppendLine($"  [OK] Внесено {Num(report.Deposit.Assets)}, знято {Num(report.Withdraw.Assets)} {assetSymbol}.");
        sb.AppendLine($"       Прибуток від компаундингу: +{Num(profit)} {assetSymbol} (+{percent.ToString("0.####", Culture)}%).");
        sb.AppendLine();
        sb.AppendLine("Auto-compounding підтверджено: кількість акцій не змінилась, але totalAssets зріс,");
        sb.AppendLine("тому кожна акція подорожчала, і інвестор може зняти більше, ніж поклав.");
        sb.AppendLine();

        return sb.ToString();
    }

    public string Render(StandReport report)
    {
        var sb = new StringBuilder();
        var assetSymbol = report.TokenA.Symbol;

        RenderInfrastructure(sb, report.Network, report.UserAddress, report.RouterAddress,
            report.TokenA, report.TokenB, report.Pool, report.Vault);

        Header(sb, "КРОК 3. ДЕПОЗИТ У СХОВИЩЕ (deposit)");
        sb.AppendLine($"  Внесено...............: {Num(report.Deposit.Assets)} {assetSymbol}");
        sb.AppendLine($"  Отримано акцій........: {Num(report.Deposit.Shares)}");
        sb.AppendLine($"  Транзакція............: {report.Deposit.TransactionHash} (gas: {report.Deposit.GasUsed})");
        RenderPosition(sb, report.PositionAfterDeposit, assetSymbol);

        sb.AppendLine();
        sb.AppendLine("Стенд готовий. Далі, у двох окремих терміналах:");
        sb.AppendLine("  1) dotnet run -- --bot      (запустити бота-кіпера)");
        sb.AppendLine("  2) dotnet run -- --donate   (імітація фарму: переказ винагороди на сховище)");
        sb.AppendLine();

        return sb.ToString();
    }

    public string Render(DonationReport report)
    {
        var sb = new StringBuilder();

        Header(sb, "ІМІТАЦІЯ ФАРМУ (прямий переказ винагороди на сховище)");
        sb.AppendLine($"  Мережа................: {report.Network}");
        sb.AppendLine($"  Сховище...............: {report.VaultAddress}");
        sb.AppendLine($"  Надіслано.............: {Num(report.Reward.Amount)} {report.RewardSymbol}");
        sb.AppendLine($"  Баланс винагороди.....: {Num(report.Reward.RewardBalanceInVault)} {report.RewardSymbol}");
        sb.AppendLine($"  Транзакція............: {report.Reward.TransactionHash}");
        sb.AppendLine();
        sb.AppendLine("  Бот-кіпер має помітити винагороду й викликати compound().");
        sb.AppendLine();

        return sb.ToString();
    }

    private static void RenderInfrastructure(
        StringBuilder sb, string network, string user, string router,
        TokenDeploymentResult tokenA, TokenDeploymentResult tokenB,
        LiquidityResult? pool, VaultDeploymentResult vault)
    {
        Header(sb, "МЕРЕЖА ТА АКАУНТ");
        sb.AppendLine($"  Мережа................: {network}");
        sb.AppendLine($"  Інвестор..............: {user}");
        sb.AppendLine($"  Router (зовнішній DEX): {router}");

        Header(sb, "КРОК 1. ТОКЕНИ ТА ПУЛ ЛІКВІДНОСТІ У ЗОВНІШНЬОМУ DEX");
        RenderToken(sb, tokenA, "Токен A (базовий актив)");
        RenderToken(sb, tokenB, "Токен B (винагорода)");
        if (pool is null)
        {
            sb.AppendLine("  Пул A/B...............: уже наповнений раніше (addLiquidity пропущено)");
        }
        else
        {
            sb.AppendLine($"  Пул A/B...............: {Num(pool.AmountA)} {tokenA.Symbol} + {Num(pool.AmountB)} {tokenB.Symbol}");
            sb.AppendLine($"    Транзакція..........: {pool.TransactionHash} (gas: {pool.GasUsed})");
        }

        Header(sb, "КРОК 2. РОЗГОРТАННЯ СХОВИЩА (AutoCompoundVault)");
        sb.AppendLine($"  Адреса сховища........: {vault.Address}");
        sb.AppendLine($"  Статус................: {(vault.WasAlreadyDeployed ? "перевикористано з файлу стану" : "розгорнуто щойно")}");
        if (vault.TransactionHash is not null)
        {
            sb.AppendLine($"  Транзакція деплою.....: {vault.TransactionHash}");
        }
    }

    private static void RenderToken(StringBuilder sb, TokenDeploymentResult token, string label)
    {
        sb.AppendLine($"  {label}: {token.Name} ({token.Symbol})");
        sb.AppendLine($"    Адреса..............: {token.Address}");
        sb.AppendLine($"    Статус..............: {(token.WasAlreadyDeployed ? "перевикористано" : "розгорнуто щойно")}");
    }

    private static void RenderPosition(StringBuilder sb, VaultSnapshot position, string assetSymbol)
    {
        sb.AppendLine("  Стан сховища та позиції:");
        sb.AppendLine($"    Акції інвестора.....: {Num(position.Shares)}");
        sb.AppendLine($"    convertToAssets.....: {Num(position.AssetsValue)} {assetSymbol}");
        sb.AppendLine($"    totalAssets.........: {Num(position.TotalAssets)} {assetSymbol}");
        sb.AppendLine($"    totalSupply акцій...: {Num(position.TotalShares)}");
        sb.AppendLine($"    Ціна 1 акції........: {Num(position.SharePrice)} {assetSymbol}");
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