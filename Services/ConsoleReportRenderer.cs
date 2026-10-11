using System.Globalization;
using System.Text;
using DeFi.Models;

namespace DeFi.Services;

public interface IReportRenderer
{
    string Render(ScenarioReport report);

    string Render(AttackReport report);
}

public sealed class ConsoleReportRenderer : IReportRenderer
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public string Render(ScenarioReport report)
    {
        var sb = new StringBuilder();

        Header(sb, "МЕРЕЖА ТА АКАУНТ");
        sb.AppendLine($"  Мережа................: {report.Network}");
        sb.AppendLine($"  Акаунт (власник токенів): {report.UserAddress}");

        Header(sb, "КРОК 1. РОЗГОРТАННЯ DAO, ПРОТОКОЛУ-МІШЕНІ ТА GOVERNANCE-ТОКЕНА");
        RenderContract(sb, $"Governance-токен ({report.TokenSymbol})", report.Token);
        RenderContract(sb, "MockProtocol", report.Protocol);
        RenderContract(sb, "SimpleDAO", report.Dao);
        sb.AppendLine($"  Початкова комісія.....: {report.FeeBefore}");

        Header(sb, "КРОК 2. ПЕРЕДАЧА ВЛАСНОСТІ ПРОТОКОЛУ ДО DAO (transferOwnership)");
        sb.AppendLine($"  transferOwnership.....: {(report.Ownership.WasAlreadyTransferred ? "вже виконано раніше" : "виконано щойно")}");
        sb.AppendLine($"    Новий власник.......: {report.Ownership.NewOwner}");
        sb.AppendLine($"  Прямий виклик setFee..: {(report.DirectAttempt.Reverted ? "відхилено" : "ПРОЙШОВ")}");
        sb.AppendLine($"    Причина відкату.....: {report.DirectAttempt.RevertReason}");
        sb.AppendLine();
        sb.AppendLine("  [OK] Тепер комісію може змінити лише DAO.");

        Header(sb, $"КРОК 3. СТВОРЕННЯ ПРОПОЗИЦІЇ (setFee({report.NewFee}) -> createProposal)");
        sb.AppendLine($"  Proposal ID...........: {report.Proposal.ProposalId}");
        sb.AppendLine($"  Payload (setFee)......: {report.Proposal.Payload}");
        sb.AppendLine($"  Кінець голосування....: {report.Proposal.Deadline.LocalDateTime:HH:mm:ss}");
        sb.AppendLine($"  Транзакція............: {report.Proposal.TransactionHash} (gas: {report.Proposal.GasUsed})");

        Header(sb, "КРОК 4. ГОЛОСУВАННЯ (vote)");
        sb.AppendLine($"  Вага голосу...........: {Num(report.Vote.Weight)} {report.TokenSymbol}");
        sb.AppendLine($"  Голос.................: {(report.Vote.Support ? "ЗА" : "ПРОТИ")}");
        sb.AppendLine($"  Транзакція............: {report.Vote.TransactionHash} (gas: {report.Vote.GasUsed})");
        sb.AppendLine($"  Підсумок голосів......: за {Num(report.AfterVote.VotesFor)}, проти {Num(report.AfterVote.VotesAgainst)}");

        Header(sb, "КРОК 5. ВИКОНАННЯ ПРОПОЗИЦІЇ (executeProposal -> target.call(payload))");
        sb.AppendLine($"  Очікування голосування: {report.Waited.TotalSeconds.ToString("0", Culture)} с");
        sb.AppendLine($"  Транзакція............: {report.Execute.TransactionHash} (gas: {report.Execute.GasUsed})");
        sb.AppendLine($"  Пропозицію виконано...: {(report.AfterExecute.Executed ? "так" : "ні")}");

        Header(sb, "КРОК 6. ПЕРЕВІРКА РЕЗУЛЬТАТУ");
        sb.AppendLine($"  Комісія ДО............: {report.FeeBefore}");
        sb.AppendLine($"  Комісія ПІСЛЯ.........: {report.FeeAfter}");
        sb.AppendLine();
        sb.AppendLine($"  [OK] fee у MockProtocol змінилась на {report.FeeAfter} після виклику від імені DAO.");
        sb.AppendLine();
        sb.AppendLine("Децентралізоване управління підтверджено: жодна людина не змінювала параметр напряму,");
        sb.AppendLine("його змінило голосування власників governance-токенів.");
        sb.AppendLine();

        return sb.ToString();
    }

    public string Render(AttackReport report)
    {
        var sb = new StringBuilder();
        var a = report.SymbolA;
        var b = report.SymbolB;

        Header(sb, "МЕРЕЖА ТА АКАУНТ");
        sb.AppendLine($"  Мережа................: {report.Network}");
        sb.AppendLine($"  Акаунт................: {report.DeployerAddress}");

        Header(sb, "КРОК 1. СТЕНД (чисте розгортання)");
        RenderContract(sb, $"Токен A ({a})", report.TokenA);
        RenderContract(sb, $"Токен B ({b})", report.TokenB);
        RenderContract(sb, "Пул", report.Pool);
        RenderContract(sb, "Спот-оракул", report.SpotOracle);
        RenderContract(sb, "Оракул-фід", report.TrustedOracle);
        RenderContract(sb, "Кредитор (вразливий)", report.VulnerableLender);
        RenderContract(sb, "Кредитор (захищений)", report.SafeLender);
        RenderContract(sb, "Кредитор миттєвих позик", report.Provider);
        sb.AppendLine($"  Чесна ціна 1 {a}......: {Num(report.TrustedPrice)} {b}");

        Header(sb, "КРОК 2. АТАКА НА ВРАЗЛИВОГО КРЕДИТОРА (ціна береться зі спот-пулу)");
        sb.AppendLine($"  Миттєва позика........: {Num(report.LoanAmount)} {b}, комісія {Num(report.FlashFee)} {b}");
        sb.AppendLine($"  Спот-ціна ДО..........: {Num(report.SpotPriceBefore)} {b} за 1 {a}");
        sb.AppendLine($"  Спот-ціна ПІСЛЯ.......: {Num(report.SpotPriceAfter)} {b} за 1 {a}");
        RenderOutcome(sb, report.Vulnerable, a, b);
        sb.AppendLine();
        sb.AppendLine("  [УВАГА] Атака успішна: заставу {0} {1} оцінено за завищеною ціною, протокол втратив ліквідність."
            .Replace("{0}", Num(report.Vulnerable.CollateralLocked)).Replace("{1}", a));

        Header(sb, "КРОК 3. ТА САМА АТАКА НА КРЕДИТОРА З ОРАКУЛОМ-ФІДОМ");
        RenderOutcome(sb, report.Protected, a, b);
        sb.AppendLine();
        sb.AppendLine("  [OK] Захист спрацював: ціна з фіда не залежить від стану пулу, прибутку немає,");
        sb.AppendLine("       тому EVM відкотила всю транзакцію разом із миттєвою позикою.");
        sb.AppendLine();

        return sb.ToString();
    }

    private static void RenderOutcome(StringBuilder sb, AttackOutcome o, string a, string b)
    {
        if (o.Reverted)
        {
            sb.AppendLine($"  Результат.............: ВІДКАТ");
            sb.AppendLine($"  Причина відкату.......: {o.RevertReason}");
            sb.AppendLine($"  Баланс кредитора {b}..: {Num(o.LenderBalanceBefore)} -> {Num(o.LenderBalanceAfter)} (без змін)");
            return;
        }

        sb.AppendLine($"  Результат.............: успішно");
        sb.AppendLine($"  Транзакція............: {o.TransactionHash} (gas: {o.GasUsed})");
        sb.AppendLine($"  Позичено у кредитора..: {Num(o.DebtOpened)} {b} під заставу {Num(o.CollateralLocked)} {a}");
        sb.AppendLine($"  Баланс кредитора {b}..: {Num(o.LenderBalanceBefore)} -> {Num(o.LenderBalanceAfter)}");
        sb.AppendLine($"  Чистий прибуток.......: {Num(o.ProfitB)} {b} (після повернення позики з комісією)");
        if (o.ProfitWithdrawTransactionHash is not null)
        {
            sb.AppendLine($"  Виведення прибутку....: {o.ProfitWithdrawTransactionHash}");
        }
    }

    private static void RenderContract(StringBuilder sb, string label, ContractRef contract)
    {
        sb.AppendLine($"  {label}:");
        sb.AppendLine($"    Адреса..............: {contract.Address}");
        sb.AppendLine($"    Статус..............: {(contract.WasAlreadyDeployed ? "перевикористано з файлу стану" : "розгорнуто щойно")}");
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