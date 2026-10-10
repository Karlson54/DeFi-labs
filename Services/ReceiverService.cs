using DeFi.Models;
using DeFi.Models.Contracts;
using Microsoft.Extensions.Options;
using Nethereum.Contracts;

namespace DeFi.Services;

public interface IReceiverService
{
    Task<ReceiverDeploymentResult> DeployAsync(string routerAddress, CancellationToken cancellationToken = default);

    Task<ReceivedMessage?> GetLastMessageAsync(string receiverAddress, CancellationToken cancellationToken = default);
}

public sealed class ReceiverService : IReceiverService
{
    private readonly IWeb3Factory _web3Factory;
    private readonly IContractArtifactProvider _artifacts;
    private readonly TimeSpan _timeout;

    public ReceiverService(IWeb3Factory web3Factory, IContractArtifactProvider artifacts, IOptions<Web3Settings> options)
    {
        _web3Factory = web3Factory;
        _artifacts = artifacts;
        _timeout = TimeSpan.FromSeconds(options.Value.TransactionTimeoutSeconds);
    }

    public async Task<ReceiverDeploymentResult> DeployAsync(string routerAddress, CancellationToken cancellationToken = default)
    {
        var artifact = await _artifacts.GetAsync("CrossChainReceiver", cancellationToken);

        var deployment = new CrossChainReceiverDeployment(artifact.Bytecode)
        {
            Router = routerAddress
        };

        var receipt = await _web3Factory.DestinationClient.Eth
            .GetContractDeploymentHandler<CrossChainReceiverDeployment>()
            .SendRequestAndWaitForReceiptAsync(deployment)
            .WaitAsync(_timeout, cancellationToken);

        if (receipt.Status?.Value != 1)
        {
            throw new InvalidOperationException(
                "Розгортання CrossChainReceiver у цільовій мережі відхилено (status = 0). " +
                "Перевірте Lab8Settings:Destination:RouterAddress, баланс ETH у L2 та байткод артефакту.");
        }

        return new ReceiverDeploymentResult(
            receipt.ContractAddress, routerAddress,
            WasAlreadyDeployed: false, receipt.TransactionHash);
    }

    public async Task<ReceivedMessage?> GetLastMessageAsync(string receiverAddress, CancellationToken cancellationToken = default)
    {
        var client = _web3Factory.DestinationClient;

        var idBytes = await client.Eth
            .GetContractQueryHandler<LastMessageIdFunction>()
            .QueryAsync<byte[]>(receiverAddress, new LastMessageIdFunction())
            .WaitAsync(_timeout, cancellationToken);

        if (idBytes is null || idBytes.All(b => b == 0))
        {
            return null;
        }

        var selector = await client.Eth
            .GetContractQueryHandler<LastSourceChainSelectorFunction>()
            .QueryAsync<ulong>(receiverAddress, new LastSourceChainSelectorFunction())
            .WaitAsync(_timeout, cancellationToken);

        var sender = await client.Eth
            .GetContractQueryHandler<LastSenderFunction>()
            .QueryAsync<string>(receiverAddress, new LastSenderFunction())
            .WaitAsync(_timeout, cancellationToken);

        var text = await client.Eth
            .GetContractQueryHandler<LastTextFunction>()
            .QueryAsync<string>(receiverAddress, new LastTextFunction())
            .WaitAsync(_timeout, cancellationToken);

        return new ReceivedMessage(
            "0x" + Convert.ToHexString(idBytes).ToLowerInvariant(),
            selector, sender, text);
    }
}