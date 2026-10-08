using DeFi.Models;
using Microsoft.Extensions.Options;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;

namespace DeFi.Services;

public interface IWeb3Factory
{
    Web3 Client { get; }

    string AccountAddress { get; }

    long ChainId { get; }

    Web3 CreateClient(string? privateKey, string settingName);

    string GetAddress(string? privateKey, string settingName);
}

public sealed class Web3Factory : IWeb3Factory
{
    private readonly Lazy<Web3> _client;
    private readonly Lazy<Account> _account;
    private readonly Web3Settings _settings;

    public Web3Factory(IOptions<Web3Settings> options)
    {
        _settings = options.Value;

        _account = new Lazy<Account>(() => CreateAccount(_settings.PrivateKey, "Web3Settings:PrivateKey"));
        _client = new Lazy<Web3>(() => new Web3(_account.Value, _settings.RpcUrl));
    }

    public Web3 Client => _client.Value;

    public string AccountAddress => _account.Value.Address;

    public long ChainId => _settings.ChainId;

    public Web3 CreateClient(string? privateKey, string settingName) =>
        new Web3(CreateAccount(privateKey, settingName), _settings.RpcUrl);

    public string GetAddress(string? privateKey, string settingName) =>
        CreateAccount(privateKey, settingName).Address;

    private Account CreateAccount(string? privateKey, string settingName)
    {
        var key = privateKey?.Trim();

        if (string.IsNullOrWhiteSpace(key) || key.StartsWith("ВАШ", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"У appsettings.json не заданий приватний ключ ({settingName}). " +
                "Використовуйте лише тестові ключі. Реальний ключ від гаманця з коштами " +
                "в конфіг класти не можна, і файл із ним не можна комітити в git.");
        }

        return new Account(key, _settings.ChainId);
    }
}