using System.Numerics;
using DeFi.Models;
using DeFi.Models.Contracts;
using Microsoft.Extensions.Options;
using Nethereum.Web3;

namespace DeFi.Services;

public interface ITokenService
{
    Task<TokenDeploymentResult> DeployAsync(TokenSettings settings, CancellationToken cancellationToken = default);
    Task<TokenDeploymentResult> DescribeAsync(TokenSettings settings, string address, CancellationToken cancellationToken = default);
    Task<decimal> GetBalanceAsync(string tokenAddress, string account, CancellationToken cancellationToken = default);
    Task<string> ApproveAsync(string tokenAddress, string spender, decimal amount, CancellationToken cancellationToken = default);
    Task<string> TransferAsync(string tokenAddress, string to, decimal amount, CancellationToken cancellationToken = default);
}

public sealed class TokenService : ITokenService
{
    private const int Decimals = 18;

    private readonly IWeb3Factory _web3Factory;
    private readonly IContractArtifactProvider _artifacts;
    private readonly TimeSpan _timeout;

    public TokenService(IWeb3Factory web3Factory, IContractArtifactProvider artifacts, IOptions<Web3Settings> options)
    {
        _web3Factory = web3Factory;
        _artifacts = artifacts;
        _timeout = TimeSpan.FromSeconds(options.Value.TransactionTimeoutSeconds);
    }

    public async Task<TokenDeploymentResult> DeployAsync(TokenSettings settings, CancellationToken cancellationToken = default)
    {
        var artifact = await _artifacts.GetAsync("AssetToken", cancellationToken);
        var web3 = _web3Factory.Client;

        var deployment = new AssetTokenDeployment(artifact.Bytecode)
        {
            Name = settings.Name,
            Symbol = settings.Symbol,
            InitialSupply = Web3.Convert.ToWei(settings.InitialSupply, Decimals)
        };

        var receipt = await web3.Eth
            .GetContractDeploymentHandler<AssetTokenDeployment>()
            .SendRequestAndWaitForReceiptAsync(deployment)
            .WaitAsync(_timeout, cancellationToken);

        if (receipt.Status?.Value != 1)
        {
            throw new InvalidOperationException(
                $"Розгортання токена {settings.Symbol} відхилено мережею (status = 0). " +
                "Найчастіша причина — некоректний байткод у артефакті або нестача коштів на газ.");
        }

        return new TokenDeploymentResult(
            settings.Name,
            settings.Symbol,
            receipt.ContractAddress,
            settings.InitialSupply,
            WasAlreadyDeployed: false,
            receipt.TransactionHash);
    }

    public async Task<TokenDeploymentResult> DescribeAsync(TokenSettings settings, string address, CancellationToken cancellationToken = default)
    {
        var supply = await QueryAsync<TotalSupplyFunction>(address, new TotalSupplyFunction(), cancellationToken);

        return new TokenDeploymentResult(
            settings.Name,
            settings.Symbol,
            address,
            Web3.Convert.FromWei(supply, Decimals),
            WasAlreadyDeployed: true,
            TransactionHash: null);
    }

    public async Task<decimal> GetBalanceAsync(string tokenAddress, string account, CancellationToken cancellationToken = default)
    {
        var raw = await QueryAsync(tokenAddress, new BalanceOfFunction { Account = account }, cancellationToken);
        return Web3.Convert.FromWei(raw, Decimals);
    }

    public async Task<string> ApproveAsync(string tokenAddress, string spender, decimal amount, CancellationToken cancellationToken = default)
    {
        var function = new ApproveFunction
        {
            Spender = spender,
            Amount = Web3.Convert.ToWei(amount, Decimals)
        };

        var receipt = await _web3Factory.Client.Eth
            .GetContractTransactionHandler<ApproveFunction>()
            .SendRequestAndWaitForReceiptAsync(tokenAddress, function)
            .WaitAsync(_timeout, cancellationToken);

        return receipt.TransactionHash;
    }

    public async Task<string> TransferAsync(string tokenAddress, string to, decimal amount, CancellationToken cancellationToken = default)
    {
        var function = new TransferFunction
        {
            To = to,
            Amount = Web3.Convert.ToWei(amount, Decimals)
        };

        var receipt = await _web3Factory.Client.Eth
            .GetContractTransactionHandler<TransferFunction>()
            .SendRequestAndWaitForReceiptAsync(tokenAddress, function)
            .WaitAsync(_timeout, cancellationToken);

        return receipt.TransactionHash;
    }

    private async Task<BigInteger> QueryAsync<TFunction>(string contractAddress, TFunction function, CancellationToken cancellationToken)
        where TFunction : Nethereum.Contracts.FunctionMessage, new()
    {
        return await _web3Factory.Client.Eth
            .GetContractQueryHandler<TFunction>()
            .QueryAsync<BigInteger>(contractAddress, function)
            .WaitAsync(_timeout, cancellationToken);
    }
}
