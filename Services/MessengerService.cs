using System.Numerics;
using DeFi.Models;
using DeFi.Models.Contracts;
using Microsoft.Extensions.Options;
using Nethereum.Contracts;
using Nethereum.Web3;

namespace DeFi.Services;

public interface IMessengerService
{
    Task<MessengerDeploymentResult> DeployAsync(string routerAddress, string linkAddress, CancellationToken cancellationToken = default);

    Task<decimal> GetFeeAsync(string messengerAddress, ulong chainSelector, string receiver, string text, CancellationToken cancellationToken = default);

    Task<SendResult> SendMessageAsync(string messengerAddress, ulong chainSelector, string receiver, string text, CancellationToken cancellationToken = default);
}

public sealed class MessengerService : IMessengerService
{
    private const int Decimals = 18;

    private readonly IWeb3Factory _web3Factory;
    private readonly IContractArtifactProvider _artifacts;
    private readonly TimeSpan _timeout;

    public MessengerService(IWeb3Factory web3Factory, IContractArtifactProvider artifacts, IOptions<Web3Settings> options)
    {
        _web3Factory = web3Factory;
        _artifacts = artifacts;
        _timeout = TimeSpan.FromSeconds(options.Value.TransactionTimeoutSeconds);
    }

    public async Task<MessengerDeploymentResult> DeployAsync(string routerAddress, string linkAddress, CancellationToken cancellationToken = default)
    {
        var artifact = await _artifacts.GetAsync("CrossChainMessenger", cancellationToken);

        var deployment = new CrossChainMessengerDeployment(artifact.Bytecode)
        {
            Router = routerAddress,
            Link = linkAddress
        };

        var receipt = await _web3Factory.Client.Eth
            .GetContractDeploymentHandler<CrossChainMessengerDeployment>()
            .SendRequestAndWaitForReceiptAsync(deployment)
            .WaitAsync(_timeout, cancellationToken);

        if (receipt.Status?.Value != 1)
        {
            throw new InvalidOperationException(
                "Розгортання CrossChainMessenger відхилено мережею (status = 0). " +
                "Перевірте адреси Router-а та LINK у Lab8Settings:Source і коректність байткоду артефакту.");
        }

        return new MessengerDeploymentResult(
            receipt.ContractAddress, routerAddress, linkAddress,
            WasAlreadyDeployed: false, receipt.TransactionHash);
    }

    public async Task<decimal> GetFeeAsync(string messengerAddress, ulong chainSelector, string receiver, string text, CancellationToken cancellationToken = default)
    {
        var function = new GetFeeFunction
        {
            DestinationChainSelector = chainSelector,
            Receiver = receiver,
            Text = text
        };

        var feeWei = await _web3Factory.Client.Eth
            .GetContractQueryHandler<GetFeeFunction>()
            .QueryAsync<BigInteger>(messengerAddress, function)
            .WaitAsync(_timeout, cancellationToken);

        return Web3.Convert.FromWei(feeWei, Decimals);
    }

    public async Task<SendResult> SendMessageAsync(string messengerAddress, ulong chainSelector, string receiver, string text, CancellationToken cancellationToken = default)
    {
        var function = new SendMessageFunction
        {
            DestinationChainSelector = chainSelector,
            Receiver = receiver,
            Text = text
        };

        var receipt = await _web3Factory.Client.Eth
            .GetContractTransactionHandler<SendMessageFunction>()
            .SendRequestAndWaitForReceiptAsync(messengerAddress, function)
            .WaitAsync(_timeout, cancellationToken);

        if (receipt.Status?.Value != 1)
        {
            throw new InvalidOperationException(
                "Транзакція sendMessage відхилена мережею. Найімовірніші причини: у контракту-месенджера " +
                "бракує LINK для комісії, цільова мережа (chain selector) не підтримується цим Router-ом, " +
                "або викликає не власник контракту.");
        }

        var sent = receipt.DecodeAllEvents<MessageSentEventDto>().FirstOrDefault()
            ?? throw new InvalidOperationException(
                "У чеку транзакції немає події MessageSent. Схоже, в артефакті лежить байткод іншої версії контракту.");

        return new SendResult(
            "0x" + Convert.ToHexString(sent.Event.MessageId).ToLowerInvariant(),
            text,
            Web3.Convert.FromWei(sent.Event.Fees, Decimals),
            receipt.TransactionHash,
            receipt.BlockNumber?.Value ?? BigInteger.Zero,
            receipt.GasUsed?.Value ?? BigInteger.Zero);
    }
}