using DeFi.Models;
using Microsoft.Extensions.Options;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;

namespace DeFi.Services;

public interface IWeb3Factory
{
    /// <summary>Клієнт вихідної мережі (L1, Sepolia).</summary>
    Web3 Client { get; }

    /// <summary>Клієнт цільової мережі (L2, Arbitrum Sepolia).</summary>
    Web3 DestinationClient { get; }

    string AccountAddress { get; }

    long ChainId { get; }

    long DestinationChainId { get; }
}

/// <summary>
/// Створює два Web3-клієнти з ОДНИМ приватним ключем. EVM-адреса акаунта
/// однакова в усіх EVM-мережах, відрізняються лише RPC та Chain ID (ключова ідея лаб. №8).
/// </summary>
public sealed class Web3Factory : IWeb3Factory
{
    private readonly Lazy<Web3> _client;
    private readonly Lazy<Web3> _destinationClient;
    private readonly Lazy<Account> _account;
    private readonly Web3Settings _settings;
    private readonly DestinationChainSettings _destination;

    public Web3Factory(IOptions<Web3Settings> web3Options, IOptions<Lab8Settings> labOptions)
    {
        _settings = web3Options.Value;
        _destination = labOptions.Value.Destination;

        _account = new Lazy<Account>(() => CreateAccount(_settings.ChainId));
        _client = new Lazy<Web3>(() => new Web3(_account.Value, _settings.RpcUrl));

        _destinationClient = new Lazy<Web3>(() =>
        {
            if (string.IsNullOrWhiteSpace(_destination.RpcUrl) ||
                _destination.RpcUrl.Contains("ВАШ", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "У appsettings.json не задано Lab8Settings:Destination:RpcUrl — RPC-endpoint цільової L2-мережі.");
            }

            return new Web3(CreateAccount(_destination.ChainId), _destination.RpcUrl);
        });
    }

    public Web3 Client => _client.Value;

    public Web3 DestinationClient => _destinationClient.Value;

    public string AccountAddress => _account.Value.Address;

    public long ChainId => _settings.ChainId;

    public long DestinationChainId => _destination.ChainId;

    private Account CreateAccount(long chainId)
    {
        var key = _settings.PrivateKey?.Trim();

        if (string.IsNullOrWhiteSpace(key) || key.StartsWith("ВАШ", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "У appsettings.json не заданий приватний ключ (Web3Settings:PrivateKey). " +
                "Використовуйте лише тестовий ключ. Реальний ключ від гаманця з коштами " +
                "в конфіг класти не можна, і файл із ним не можна комітити в git.");
        }

        // Chain ID входить у підпис транзакції (EIP-155), тому для кожної мережі свій Account.
        return new Account(key, chainId);
    }
}