using System.Numerics;
using DeFi.Models;
using DeFi.Models.Contracts;
using Microsoft.Extensions.Options;

namespace DeFi.Services;

public interface IStablecoinService
{
    Task<StablecoinDeploymentResult> DeployAsync(StablecoinSettings settings, CancellationToken cancellationToken = default);

    StablecoinDeploymentResult Describe(StablecoinSettings settings, string address);

    Task<OwnershipTransferResult> TransferOwnershipAsync(string tokenAddress, string newOwner, CancellationToken cancellationToken = default);

    Task<BigInteger> BalanceOfAsync(string tokenAddress, string account, CancellationToken cancellationToken = default);

    Task<string> ApproveAsync(string tokenAddress, string spender, BigInteger amountWei, CancellationToken cancellationToken = default);
}

public sealed class StablecoinService : IStablecoinService
{
    private readonly IWeb3Factory _web3Factory;
    private readonly IContractArtifactProvider _artifacts;
    private readonly TimeSpan _timeout;

    public StablecoinService(IWeb3Factory web3Factory, IContractArtifactProvider artifacts, IOptions<Web3Settings> options)
    {
        _web3Factory = web3Factory;
        _artifacts = artifacts;
        _timeout = TimeSpan.FromSeconds(options.Value.TransactionTimeoutSeconds);
    }

    public async Task<StablecoinDeploymentResult> DeployAsync(StablecoinSettings settings, CancellationToken cancellationToken = default)
    {
        var artifact = await _artifacts.GetAsync("StableCoin", cancellationToken);

        var deployment = new StableCoinDeployment(artifact.Bytecode)
        {
            Name = settings.Name,
            Symbol = settings.Symbol
        };

        var receipt = await _web3Factory.Client.Eth
            .GetContractDeploymentHandler<StableCoinDeployment>()
            .SendRequestAndWaitForReceiptAsync(deployment)
            .WaitAsync(_timeout, cancellationToken);

        if (receipt.Status?.Value != 1)
        {
            throw new InvalidOperationException(
                $"Розгортання стейблкоїна {settings.Symbol} відхилено мережею (status = 0). " +
                "Найчастіша причина — некоректний байткод у артефакті або нестача коштів на газ.");
        }

        return new StablecoinDeploymentResult(
            settings.Name, settings.Symbol, receipt.ContractAddress,
            WasAlreadyDeployed: false, receipt.TransactionHash);
    }

    public StablecoinDeploymentResult Describe(StablecoinSettings settings, string address) =>
        new(settings.Name, settings.Symbol, address, WasAlreadyDeployed: true, TransactionHash: null);

    public async Task<OwnershipTransferResult> TransferOwnershipAsync(string tokenAddress, string newOwner, CancellationToken cancellationToken = default)
    {
        var currentOwner = await _web3Factory.Client.Eth
            .GetContractQueryHandler<OwnerFunction>()
            .QueryAsync<string>(tokenAddress, new OwnerFunction())
            .WaitAsync(_timeout, cancellationToken);

        if (string.Equals(currentOwner, newOwner, StringComparison.OrdinalIgnoreCase))
        {
            return new OwnershipTransferResult(newOwner, WasAlreadyTransferred: true, TransactionHash: null);
        }

        var function = new TransferOwnershipFunction { NewOwner = newOwner };

        var receipt = await _web3Factory.Client.Eth
            .GetContractTransactionHandler<TransferOwnershipFunction>()
            .SendRequestAndWaitForReceiptAsync(tokenAddress, function)
            .WaitAsync(_timeout, cancellationToken);

        if (receipt.Status?.Value != 1)
        {
            throw new InvalidOperationException(
                "transferOwnership відхилено мережею. Права власності на стейблкоїн " +
                "може передати лише поточний власник (акаунт-розгортач).");
        }

        return new OwnershipTransferResult(newOwner, WasAlreadyTransferred: false, receipt.TransactionHash);
    }

    public async Task<BigInteger> BalanceOfAsync(string tokenAddress, string account, CancellationToken cancellationToken = default)
    {
        return await _web3Factory.Client.Eth
            .GetContractQueryHandler<StableCoinBalanceOfFunction>()
            .QueryAsync<BigInteger>(tokenAddress, new StableCoinBalanceOfFunction { Account = account })
            .WaitAsync(_timeout, cancellationToken);
    }

    public async Task<string> ApproveAsync(string tokenAddress, string spender, BigInteger amountWei, CancellationToken cancellationToken = default)
    {
        var function = new StableCoinApproveFunction
        {
            Spender = spender,
            Amount = amountWei
        };

        var receipt = await _web3Factory.Client.Eth
            .GetContractTransactionHandler<StableCoinApproveFunction>()
            .SendRequestAndWaitForReceiptAsync(tokenAddress, function)
            .WaitAsync(_timeout, cancellationToken);

        if (receipt.Status?.Value != 1)
        {
            throw new InvalidOperationException(
                $"Approve на токен {tokenAddress} для {spender} відхилено мережею (status = 0).");
        }

        return receipt.TransactionHash;
    }
}