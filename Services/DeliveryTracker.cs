using System.Diagnostics;
using DeFi.Models;
using Microsoft.Extensions.Options;

namespace DeFi.Services;

public interface IDeliveryTracker
{
    Task<DeliveryReport> TrackAsync(CancellationToken cancellationToken = default);
}

public sealed class DeliveryTracker : IDeliveryTracker
{
    private readonly IWeb3Factory _web3Factory;
    private readonly IReceiverService _receiverService;
    private readonly IDeploymentStateStore _stateStore;
    private readonly Lab8Settings _settings;

    public DeliveryTracker(
        IWeb3Factory web3Factory,
        IReceiverService receiverService,
        IDeploymentStateStore stateStore,
        IOptions<Lab8Settings> settingsOptions)
    {
        _web3Factory = web3Factory;
        _receiverService = receiverService;
        _stateStore = stateStore;
        _settings = settingsOptions.Value;
    }

    public async Task<DeliveryReport> TrackAsync(CancellationToken cancellationToken = default)
    {
        var state = await _stateStore.LoadAsync(_web3Factory.ChainId, _web3Factory.AccountAddress, cancellationToken);

        if (string.IsNullOrWhiteSpace(state.ReceiverAddress) || string.IsNullOrWhiteSpace(state.LastMessageId))
        {
            throw new InvalidOperationException(
                "У файлі стану немає адреси отримувача або messageId. Спочатку відправте повідомлення: dotnet run");
        }

        var receiver = state.ReceiverAddress!;
        var messageId = state.LastMessageId!;
        var explorerUrl = _settings.ExplorerMessageUrl + messageId;
        var destination = $"{_settings.Destination.Name} (chainId {_web3Factory.DestinationChainId})";

        var delay = TimeSpan.FromSeconds(Math.Max(1, _settings.TrackPollingIntervalSeconds));
        var timeout = TimeSpan.FromMinutes(Math.Max(1, _settings.TrackTimeoutMinutes));
        var stopwatch = Stopwatch.StartNew();

        Log("Запуск трекера доставки. Очікування повідомлення в цільовій мережі...");
        Log($"Отримувач.......: {receiver}");
        Log($"messageId.......: {messageId}");
        Log($"CCIP Explorer...: {explorerUrl}");
        Log($"Період опитування: {delay.TotalSeconds}с, таймаут: {timeout.TotalMinutes} хв");

        while (stopwatch.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var received = await _receiverService.GetLastMessageAsync(receiver, cancellationToken);

                if (received is null)
                {
                    Log("Отримувач | повідомлень ще немає");
                }
                else if (string.Equals(received.MessageId, messageId, StringComparison.OrdinalIgnoreCase))
                {
                    Log("[УСПІХ] Повідомлення доставлено в цільову мережу.");
                    return new DeliveryReport(destination, receiver, messageId, true, stopwatch.Elapsed, received, explorerUrl);
                }
                else
                {
                    Log($"Отримувач | останнє повідомлення інше ({received.MessageId[..10]}…), очікуємо наше");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log($"Помилка опитування: {ex.Message}");
            }

            await Task.Delay(delay, cancellationToken);
        }

        Log("[ТАЙМАУТ] Повідомлення не з'явилося за відведений час.");
        return new DeliveryReport(destination, receiver, messageId, false, stopwatch.Elapsed, null, explorerUrl);
    }

    private static void Log(string message) =>
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
}