using System.Globalization;
using System.Numerics;
using DeFi.Models;
using Microsoft.Extensions.Options;
using Nethereum.Web3;

namespace DeFi.Services;

public interface ILiquidatorBot
{
    Task RunAsync(CancellationToken cancellationToken = default);
}

public sealed class LiquidatorBot : ILiquidatorBot
{
    private const int Decimals = 18;
    private static readonly BigInteger Precision = BigInteger.Pow(10, Decimals);
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    private readonly IWeb3Factory _web3Factory;
    private readonly ILiquidatorService _liquidator;
    private readonly IDeploymentStateStore _stateStore;
    private readonly Lab6Settings _settings;

    public LiquidatorBot(
        IWeb3Factory web3Factory,
        ILiquidatorService liquidator,
        IDeploymentStateStore stateStore,
        IOptions<Lab6Settings> settingsOptions)
    {
        _web3Factory = web3Factory;
        _liquidator = liquidator;
        _stateStore = stateStore;
        _settings = settingsOptions.Value;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var borrower = _web3Factory.AccountAddress;
        var liquidator = _liquidator.Address;

        if (string.Equals(borrower, liquidator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Гаманець ліквідатора збігається з гаманцем позичальника. Протокол забороняє " +
                "самоліквідацію — вкажіть інший ключ у Lab6Settings:LiquidatorPrivateKey.");
        }

        var state = await _stateStore.LoadAsync(_web3Factory.ChainId, borrower, cancellationToken);

        if (string.IsNullOrWhiteSpace(state.EngineAddress) || string.IsNullOrWhiteSpace(state.StablecoinAddress))
        {
            throw new InvalidOperationException(
                "У deployment-state.json немає адрес контрактів. Спочатку виконайте: dotnet run");
        }

        var engine = state.EngineAddress!;
        var stablecoin = state.StablecoinAddress!;

        var users = new List<string> { borrower };
        users.AddRange(_settings.MonitoredUsers.Where(u => !string.IsNullOrWhiteSpace(u)));
        users = users.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var delay = TimeSpan.FromSeconds(Math.Max(1, _settings.BotPollingIntervalSeconds));

        Log("Запуск бота-ліквідатора. Очікування зміни ціни...");
        Log($"Ліквідатор.....: {liquidator}");
        Log($"StableEngine...: {engine}");
        Log($"Позицій у нагляді: {users.Count}, період опитування: {delay.TotalSeconds}с");

        while (!cancellationToken.IsCancellationRequested)
        {
            var liquidated = false;

            foreach (var user in users)
            {
                try
                {
                    liquidated |= await CheckUserAsync(engine, stablecoin, user, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log($"Помилка перевірки позиції {user}: {ex.Message}");
                }
            }

            if (liquidated && _settings.StopAfterFirstLiquidation)
            {
                Log("Ліквідацію виконано. Бот зупиняється (Lab6Settings:StopAfterFirstLiquidation).");
                return;
            }

            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        Log("Бот зупинено.");
    }

    private async Task<bool> CheckUserAsync(string engine, string stablecoin, string user, CancellationToken cancellationToken)
    {
        var healthFactor = await _liquidator.GetHealthFactorAsync(engine, user, cancellationToken);

        if (healthFactor is null)
        {
            Log($"Позиція {user} | боргу немає (HF = ∞)");
            return false;
        }

        var hfDecimal = Web3.Convert.FromWei(healthFactor.Value, Decimals);
        Log($"Позиція {user} | HF: {hfDecimal.ToString("0.####", Culture)}");

        if (healthFactor.Value >= Precision)
        {
            return false;
        }

        Log($"[УВАГА] Виявлено неплатоспроможну позицію {user}! Ініціалізація ліквідації...");

        var debtWei = await _liquidator.GetDebtAsync(engine, user, cancellationToken);
        var balanceWei = await _liquidator.GetStablecoinBalanceAsync(stablecoin, cancellationToken);

        if (balanceWei < debtWei)
        {
            Log($"[ПРОПУСК] Недостатньо стейблкоїнів: потрібно {Num(Web3.Convert.FromWei(debtWei, Decimals))}, " +
                $"є {Num(Web3.Convert.FromWei(balanceWei, Decimals))}.");
            return false;
        }

        await _liquidator.ApproveAsync(stablecoin, engine, debtWei, cancellationToken);

        var result = await _liquidator.LiquidateAsync(engine, user, cancellationToken);

        var price = await _liquidator.GetEthUsdPriceAsync(engine, cancellationToken);
        var profitUsd = result.CollateralSeizedEth * price - result.DebtCovered;

        Log($"[УСПІХ] Позицію ліквідовано у блоці {result.BlockNumber}. Tx: {result.TransactionHash}");
        Log($"        Погашено боргу.....: {Num(result.DebtCovered)} {"USD"}");
        Log($"        Отримано застави...: {Num(result.CollateralSeizedEth)} ETH (курс Chainlink ${Num(price)})");
        Log($"        Бонус (до вирахування газу): ≈ ${Num(profitUsd)}, газ: {result.GasUsed}");

        return true;
    }

    private static string Num(decimal value) => value.ToString("0.########", Culture);

    private static void Log(string message) =>
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
}