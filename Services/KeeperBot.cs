using System.Globalization;
using Nethereum.Web3;
using DeFi.Models;
using Microsoft.Extensions.Options;

namespace DeFi.Services;

public interface IKeeperBot
{
    Task RunAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Off-chain кіпер (Keeper): періодично перевіряє rewardToken.balanceOf(vault)
/// і, якщо винагорода перевищила поріг, підписує транзакцію vault.compound().
/// Контракт не може виконатися "за розкладом" сам — хтось має оплатити газ.
/// </summary>
public sealed class KeeperBot : IKeeperBot
{
    private const int Decimals = 18;
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    private readonly IWeb3Factory _web3Factory;
    private readonly ITokenService _tokenService;
    private readonly IVaultService _vaultService;
    private readonly IDeploymentStateStore _stateStore;
    private readonly Lab7Settings _settings;

    public KeeperBot(
        IWeb3Factory web3Factory,
        ITokenService tokenService,
        IVaultService vaultService,
        IDeploymentStateStore stateStore,
        IOptions<Lab7Settings> settingsOptions)
    {
        _web3Factory = web3Factory;
        _tokenService = tokenService;
        _vaultService = vaultService;
        _stateStore = stateStore;
        _settings = settingsOptions.Value;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var keeper = _web3Factory.AccountAddress;
        var state = await _stateStore.LoadAsync(_web3Factory.ChainId, keeper, cancellationToken);

        if (string.IsNullOrWhiteSpace(state.VaultAddress) || string.IsNullOrWhiteSpace(state.TokenBAddress))
        {
            throw new InvalidOperationException(
                "У файлі стану немає адрес сховища й токена винагороди. Спочатку виконайте: dotnet run -- --stand");
        }

        var vault = state.VaultAddress!;
        var rewardToken = state.TokenBAddress!;
        var symbol = _settings.TokenB.Symbol;
        var delay = TimeSpan.FromSeconds(Math.Max(1, _settings.BotPollingIntervalSeconds));

        Log("Запуск бота-кіпера. Очікування винагороди на сховищі...");
        Log($"Кіпер..........: {keeper}");
        Log($"Сховище........: {vault}");
        Log($"Поріг винагороди: {Num(_settings.RewardThreshold)} {symbol}, період опитування: {delay.TotalSeconds}с");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (await CheckAndCompoundAsync(vault, rewardToken, keeper, symbol, cancellationToken) &&
                    _settings.StopAfterFirstCompound)
                {
                    Log("Реінвестування виконано. Бот зупиняється (Lab7Settings:StopAfterFirstCompound).");
                    return;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log($"Помилка циклу кіпера: {ex.Message}");
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

    private async Task<bool> CheckAndCompoundAsync(
        string vault, string rewardToken, string keeper, string symbol, CancellationToken cancellationToken)
    {
        var rewardWei = await _tokenService.BalanceOfAsync(rewardToken, vault, cancellationToken);
        var reward = Web3.Convert.FromWei(rewardWei, Decimals);

        Log($"Сховище {vault} | винагорода: {Num(reward)} {symbol}");

        if (reward <= 0m || reward < _settings.RewardThreshold)
        {
            return false;
        }

        Log($"[УВАГА] Винагорода перевищила поріг ({Num(_settings.RewardThreshold)} {symbol}). Виклик compound()...");

        var before = await _vaultService.GetSnapshotAsync(vault, keeper, cancellationToken);
        var result = await _vaultService.CompoundAsync(vault, rewardWei, cancellationToken);
        var after = await _vaultService.GetSnapshotAsync(vault, keeper, cancellationToken);

        Log($"[УСПІХ] Реінвестування виконано. Tx: {result.TransactionHash}");
        Log($"        Продано винагороди.: {Num(result.RewardSold)} {symbol}");
        Log($"        Отримано активу....: {Num(result.AssetsReceived)}");
        Log($"        totalAssets........: {Num(result.TotalAssetsBefore)} -> {Num(result.TotalAssetsAfter)}");
        Log($"        Ціна акції.........: {Num(before.SharePrice)} -> {Num(after.SharePrice)}, газ: {result.GasUsed}");

        return true;
    }

    private static string Num(decimal value) => value.ToString("0.########", Culture);

    private static void Log(string message) =>
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
}