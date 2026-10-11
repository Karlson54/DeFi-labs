using DeFi.Models;
using Microsoft.Extensions.Options;
using Nethereum.Contracts;

namespace DeFi.Services;

public sealed record DeployedContract(string Address, string TransactionHash);

public interface IContractDeployer
{
    Task<DeployedContract> DeployAsync<TMessage>(
        string artifactName, Action<TMessage> configure, string failureHint,
        CancellationToken cancellationToken = default)
        where TMessage : ContractDeploymentMessage, new();
}

public sealed class ContractDeployer : IContractDeployer
{
    private readonly IWeb3Factory _web3Factory;
    private readonly IContractArtifactProvider _artifacts;
    private readonly TimeSpan _timeout;

    public ContractDeployer(IWeb3Factory web3Factory, IContractArtifactProvider artifacts, IOptions<Web3Settings> options)
    {
        _web3Factory = web3Factory;
        _artifacts = artifacts;
        _timeout = TimeSpan.FromSeconds(options.Value.TransactionTimeoutSeconds);
    }

    public async Task<DeployedContract> DeployAsync<TMessage>(
        string artifactName, Action<TMessage> configure, string failureHint,
        CancellationToken cancellationToken = default)
        where TMessage : ContractDeploymentMessage, new()
    {
        var artifact = await _artifacts.GetAsync(artifactName, cancellationToken);

        var message = new TMessage { ByteCode = artifact.Bytecode };
        configure(message);

        var receipt = await _web3Factory.Client.Eth
            .GetContractDeploymentHandler<TMessage>()
            .SendRequestAndWaitForReceiptAsync(message)
            .WaitAsync(_timeout, cancellationToken);

        if (receipt.Status?.Value != 1)
        {
            throw new InvalidOperationException(
                $"Розгортання {artifactName} відхилено мережею (status = 0). {failureHint}");
        }

        return new DeployedContract(receipt.ContractAddress, receipt.TransactionHash);
    }
}