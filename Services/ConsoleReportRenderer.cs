using System.Globalization;
using System.Text;
using DeFi.Models;

namespace DeFi.Services;

public interface IReportRenderer
{
    string Render(ScenarioReport report);

    string Render(DeliveryReport report);
}

public sealed class ConsoleReportRenderer : IReportRenderer
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public string Render(ScenarioReport report)
    {
        var sb = new StringBuilder();

        Header(sb, "МЕРЕЖІ ТА АКАУНТ");
        sb.AppendLine($"  Вихідна мережа (L1)...: {report.SourceNetwork}");
        sb.AppendLine($"  Цільова мережа (L2)...: {report.DestinationNetwork}");
        sb.AppendLine($"  Chain Selector (CCIP).: {report.DestinationChainSelector}");
        sb.AppendLine($"  Акаунт (однаковий у L1 і L2): {report.DeployerAddress}");

        Header(sb, "КРОК 1. КОНТРАКТ-ОТРИМУВАЧ У ЦІЛЬОВІЙ L2 (CrossChainReceiver)");
        sb.AppendLine($"  Адреса................: {report.Receiver.Address}");
        sb.AppendLine($"  CCIP Router (L2)......: {report.Receiver.RouterAddress}");
        sb.AppendLine($"  Статус................: {Status(report.Receiver.WasAlreadyDeployed)}");
        if (report.Receiver.TransactionHash is not null)
        {
            sb.AppendLine($"  Транзакція деплою.....: {report.Receiver.TransactionHash}");
        }

        Header(sb, "КРОК 2. МІЖМЕРЕЖЕВИЙ МЕСЕНДЖЕР У ВИХІДНІЙ МЕРЕЖІ (CrossChainMessenger)");
        sb.AppendLine($"  Адреса................: {report.Messenger.Address}");
        sb.AppendLine($"  CCIP Router (L1)......: {report.Messenger.RouterAddress}");
        sb.AppendLine($"  Токен комісії (LINK)..: {report.Messenger.LinkTokenAddress}");
        sb.AppendLine($"  Статус................: {Status(report.Messenger.WasAlreadyDeployed)}");
        if (report.Messenger.TransactionHash is not null)
        {
            sb.AppendLine($"  Транзакція деплою.....: {report.Messenger.TransactionHash}");
        }

        Header(sb, "КРОК 3. ПОПОВНЕННЯ LINK-БАЛАНСУ МЕСЕНДЖЕРА");
        if (report.Funding.TransactionHash is null)
        {
            sb.AppendLine("  Пропущено.............: на балансі контракту вже достатньо LINK");
        }
        else
        {
            sb.AppendLine($"  Надіслано.............: {Num(report.Funding.Sent)} LINK");
            sb.AppendLine($"  Транзакція............: {report.Funding.TransactionHash}");
        }
        sb.AppendLine($"  Баланс месенджера.....: {Num(report.Funding.MessengerBalance)} LINK");
        sb.AppendLine($"  Баланс гаманця........: {Num(report.Funding.DeployerBalance)} LINK");

        Header(sb, "КРОК 4. РОЗРАХУНОК КОМІСІЇ (Router.getFee)");
        sb.AppendLine($"  Орієнтовна комісія....: {Num(report.EstimatedFeeLink)} LINK");

        Header(sb, "КРОК 5. ВІДПРАВКА ПОВІДОМЛЕННЯ (sendMessage -> Router.ccipSend)");
        sb.AppendLine($"  Текст.................: {report.Send.Text}");
        sb.AppendLine($"  Списано комісії.......: {Num(report.Send.FeeLink)} LINK");
        sb.AppendLine($"  Транзакція............: {report.Send.TransactionHash} (gas: {report.Send.GasUsed})");
        sb.AppendLine($"  Блок..................: {report.Send.BlockNumber}");
        sb.AppendLine($"  messageId.............: {report.Send.MessageId}");
        sb.AppendLine($"  CCIP Explorer.........: {report.ExplorerUrl}");

        sb.AppendLine();
        sb.AppendLine("Повідомлення поставлено в чергу децентралізованої мережі оракулів. Доставка асинхронна");
        sb.AppendLine("і триває кілька хвилин. Для автоматичного очікування виконайте: dotnet run -- --track");
        sb.AppendLine();

        return sb.ToString();
    }

    public string Render(DeliveryReport report)
    {
        var sb = new StringBuilder();

        Header(sb, "СТАТУС ДОСТАВКИ МІЖМЕРЕЖЕВОГО ПОВІДОМЛЕННЯ");
        sb.AppendLine($"  Цільова мережа........: {report.DestinationNetwork}");
        sb.AppendLine($"  Отримувач.............: {report.ReceiverAddress}");
        sb.AppendLine($"  messageId.............: {report.MessageId}");
        sb.AppendLine($"  Час очікування........: {report.Elapsed:mm\\:ss}");
        sb.AppendLine($"  CCIP Explorer.........: {report.ExplorerUrl}");

        sb.AppendLine();
        if (report.Delivered && report.Received is not null)
        {
            sb.AppendLine("  [OK] Status: Success — контракт-отримувач у цільовій мережі зберіг повідомлення.");
            sb.AppendLine($"       Текст.............: {report.Received.Text}");
            sb.AppendLine($"       Відправник (L1)...: {report.Received.Sender}");
            sb.AppendLine($"       Source selector...: {report.Received.SourceChainSelector}");
        }
        else
        {
            sb.AppendLine("  [УВАГА] Доставку не підтверджено за відведений час. Перевірте статус у CCIP Explorer");
            sb.AppendLine("          (можливо, потрібне ручне виконання через брак gasLimit) або запустіть --track ще раз.");
        }
        sb.AppendLine();

        return sb.ToString();
    }

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