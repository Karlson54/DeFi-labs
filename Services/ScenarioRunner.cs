using DeFi.Models;
using Microsoft.Extensions.Options;
using Nethereum.Web3;

namespace DeFi.Services;

public interface IScenarioRunner
{
    Task<ScenarioReport> RunAsync(CancellationToken cancellationToken = default);
}

public sealed class ScenarioRunner : IScenarioRunner
{
    private readonly IWeb3Factory _web3Factory;
    private readonly IReceiverService _receiverService;
    private readonly IMessengerService _messengerService;
    private readonly ILinkService _linkService;
    private readonly IDeploymentStateStore _stateStore;
    private readonly Lab8Settings _settings;

    public ScenarioRunner(
        IWeb3Factory web3Factory,
        IReceiverService receiverService,
        IMessengerService messengerService,
        ILinkService linkService,
        IDeploymentStateStore stateStore,
        IOptions<Lab8Settings> settingsOptions)
    {
        _web3Factory = web3Factory;
        _receiverService = receiverService;
        _messengerService = messengerService;
        _linkService = linkService;
        _stateStore = stateStore;
        _settings = settingsOptions.Value;
    }

    public async Task<ScenarioReport> RunAsync(CancellationToken cancellationToken = default)
    {
        ValidateSettings();

        var user = _web3Factory.AccountAddress;
        var chainId = _web3Factory.ChainId;
        var source = _settings.Source;
        var destination = _settings.Destination;

        var state = await _stateStore.LoadAsync(chainId, user, cancellationToken);

        var knownReceiver = SameAddress(state.DestinationRouterAddress, destination.RouterAddress)
            ? state.ReceiverAddress
            : null;
        var receiver = await EnsureReceiverAsync(knownReceiver, cancellationToken);
        state = state with { ReceiverAddress = receiver.Address, DestinationRouterAddress = destination.RouterAddress };
        await _stateStore.SaveAsync(state, cancellationToken);

        var knownMessenger =
            SameAddress(state.SourceRouterAddress, source.RouterAddress) &&
            SameAddress(state.LinkTokenAddress, source.LinkTokenAddress)
                ? state.MessengerAddress
                : null;
        var messenger = await EnsureMessengerAsync(knownMessenger, cancellationToken);
        state = state with
        {
            MessengerAddress = messenger.Address,
            SourceRouterAddress = source.RouterAddress,
            LinkTokenAddress = source.LinkTokenAddress
        };
        await _stateStore.SaveAsync(state, cancellationToken);

        var funding = await EnsureMessengerFundedAsync(messenger.Address, user, cancellationToken);

        var fee = await _messengerService.GetFeeAsync(
            messenger.Address, destination.ChainSelector, receiver.Address, _settings.MessageText, cancellationToken);

        if (fee > funding.MessengerBalance)
        {
            throw new InvalidOperationException(
                $"Комісія CCIP ({fee} LINK) перевищує баланс контракту-месенджера ({funding.MessengerBalance} LINK). " +
                "Збільште Lab8Settings:LinkFundingAmount.");
        }

        var send = await _messengerService.SendMessageAsync(
            messenger.Address, destination.ChainSelector, receiver.Address, _settings.MessageText, cancellationToken);

        state = state with { LastMessageId = send.MessageId };
        await _stateStore.SaveAsync(state, cancellationToken);

        return new ScenarioReport(
            SourceNetwork: $"chainId {chainId}",
            DestinationNetwork: $"{destination.Name} (chainId {destination.ChainId})",
            DeployerAddress: user,
            DestinationChainSelector: destination.ChainSelector,
            Receiver: receiver,
            Messenger: messenger,
            Funding: funding,
            EstimatedFeeLink: fee,
            Send: send,
            ExplorerUrl: _settings.ExplorerMessageUrl + send.MessageId);
    }

    private void ValidateSettings()
    {
        RequireAddress(_settings.Source.RouterAddress, "Lab8Settings:Source:RouterAddress");
        RequireAddress(_settings.Source.LinkTokenAddress, "Lab8Settings:Source:LinkTokenAddress");
        RequireAddress(_settings.Destination.RouterAddress, "Lab8Settings:Destination:RouterAddress");

        if (_settings.Destination.ChainSelector == 0)
        {
            throw new InvalidOperationException(
                "У Lab8Settings:Destination:ChainSelector не задано CCIP Chain Selector цільової мережі " +
                "(довідник: https://docs.chain.link/ccip/directory).");
        }

        if (string.IsNullOrWhiteSpace(_settings.MessageText))
        {
            throw new InvalidOperationException("Lab8Settings:MessageText не може бути порожнім.");
        }

        if (_settings.LinkFundingAmount <= 0)
        {
            throw new InvalidOperationException("Lab8Settings:LinkFundingAmount має бути додатним.");
        }
    }

    private static void RequireAddress(string value, string settingName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("0x_", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"У appsettings.json не задано {settingName} — візьміть адресу з CCIP Directory (docs.chain.link/ccip/directory).");
        }
    }

    private static bool SameAddress(string? a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private async Task<ReceiverDeploymentResult> EnsureReceiverAsync(string? knownAddress, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(knownAddress) &&
            await HasContractCodeAsync(_web3Factory.DestinationClient, knownAddress))
        {
            return new ReceiverDeploymentResult(
                knownAddress, _settings.Destination.RouterAddress,
                WasAlreadyDeployed: true, TransactionHash: null);
        }

        return await _receiverService.DeployAsync(_settings.Destination.RouterAddress, cancellationToken);
    }

    private async Task<MessengerDeploymentResult> EnsureMessengerAsync(string? knownAddress, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(knownAddress) &&
            await HasContractCodeAsync(_web3Factory.Client, knownAddress))
        {
            return new MessengerDeploymentResult(
                knownAddress, _settings.Source.RouterAddress, _settings.Source.LinkTokenAddress,
                WasAlreadyDeployed: true, TransactionHash: null);
        }

        return await _messengerService.DeployAsync(
            _settings.Source.RouterAddress, _settings.Source.LinkTokenAddress, cancellationToken);
    }

    private async Task<LinkFundingResult> EnsureMessengerFundedAsync(string messenger, string user, CancellationToken cancellationToken)
    {
        var client = _web3Factory.Client;

        async Task<decimal> EthBalanceAsync(string address) =>
            Web3.Convert.FromWei((await client.Eth.GetBalance.SendRequestAsync(address)).Value, 18);

        var messengerBalance = await EthBalanceAsync(messenger);
        var deployerBalance = await EthBalanceAsync(user);

        decimal sent = 0m;
        string? tx = null;

        if (messengerBalance < _settings.LinkFundingAmount)
        {
            sent = _settings.LinkFundingAmount - messengerBalance;

            if (deployerBalance < sent + 0.005m)
            {
                throw new InvalidOperationException(
                    $"Недостатньо Sepolia ETH на гаманці ({deployerBalance}): потрібно {sent} для месенджера плюс запас на газ.");
            }

            var receipt = await client.Eth.GetEtherTransferService()
                .TransferEtherAndWaitForReceiptAsync(messenger, sent, gas: new System.Numerics.BigInteger(100000))
                .WaitAsync(TimeSpan.FromMinutes(3), cancellationToken);

            if (receipt.Status?.Value != 1)
            {
                throw new InvalidOperationException("Переказ ETH на контракт-месенджер відхилено мережею (status = 0).");
            }

            tx = receipt.TransactionHash;
            messengerBalance = await EthBalanceAsync(messenger);
            deployerBalance = await EthBalanceAsync(user);
        }

        return new LinkFundingResult("native ETH", sent, tx, messengerBalance, deployerBalance);
    }

    private static async Task<bool> HasContractCodeAsync(Web3 client, string address)
    {
        var code = await client.Eth.GetCode.SendRequestAsync(address);
        return !string.IsNullOrEmpty(code) && code != "0x";
    }
}