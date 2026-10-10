using System.Numerics;
using DeFi.Models;
using DeFi.Models.Contracts;
using Microsoft.Extensions.Options;
using Nethereum.Web3;

namespace DeFi.Services;

public interface ILinkService
{
    Task<decimal> BalanceOfAsync(string tokenAddress, string account, CancellationToken cancellationToken = default);

    Task<string> TransferAsync(string tokenAddress, string to, decimal amount, CancellationToken cancellationToken = default);
}

/// <summary>Мінімальна робота з ERC-20 LINK у вихідній мережі.</summary>
public sealed class LinkService : ILinkService
{
    private const int Decimals = 18;

    private readonly IWeb3Factory _web3Factory;
    private readonly TimeSpan _timeout;

    public LinkService(IWeb3Factory web3Factory, IOptions<Web3Settings> options)
    {
        _web3Factory = web3Factory;
        _timeout = TimeSpan.FromSeconds(options.Value.TransactionTimeoutSeconds);
    }

    public async Task<decimal> BalanceOfAsync(string tokenAddress, string account, CancellationToken cancellationToken = default)
    {
        var balance = await _web3Factory.Client.Eth
            .GetContractQueryHandler<LinkBalanceOfFunction>()
            .QueryAsync<BigInteger>(tokenAddress, new LinkBalanceOfFunction { Account = account })
            .WaitAsync(_timeout, cancellationToken);

        return Web3.Convert.FromWei(balance, Decimals);
    }

    public async Task<string> TransferAsync(string tokenAddress, string to, decimal amount, CancellationToken cancellationToken = default)
    {
        var function = new LinkTransferFunction
        {
            To = to,
            Amount = Web3.Convert.ToWei(amount, Decimals)
        };

        var receipt = await _web3Factory.Client.Eth
            .GetContractTransactionHandler<LinkTransferFunction>()
            .SendRequestAndWaitForReceiptAsync(tokenAddress, function)
            .WaitAsync(_timeout, cancellationToken);

        if (receipt.Status?.Value != 1)
        {
            throw new InvalidOperationException(
                $"Transfer LINK на адресу {to} відхилено мережею (status = 0). " +
                "Перевірте, що Lab8Settings:Source:LinkTokenAddress — це LINK саме вихідної мережі.");
        }

        return receipt.TransactionHash;
    }
}