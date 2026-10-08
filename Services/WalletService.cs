using DeFi.Models;
using Microsoft.Extensions.Options;
using Nethereum.Web3;

namespace DeFi.Services;

public interface IWalletService
{
    Task<decimal> GetEthBalanceAsync(string address, CancellationToken cancellationToken = default);

    Task<string> TransferEthAsync(string to, decimal amountEth, CancellationToken cancellationToken = default);
}

public sealed class WalletService : IWalletService
{
    private const int Decimals = 18;

    private readonly IWeb3Factory _web3Factory;
    private readonly TimeSpan _timeout;

    public WalletService(IWeb3Factory web3Factory, IOptions<Web3Settings> options)
    {
        _web3Factory = web3Factory;
        _timeout = TimeSpan.FromSeconds(options.Value.TransactionTimeoutSeconds);
    }

    public async Task<decimal> GetEthBalanceAsync(string address, CancellationToken cancellationToken = default)
    {
        var balance = await _web3Factory.Client.Eth.GetBalance
            .SendRequestAsync(address)
            .WaitAsync(_timeout, cancellationToken);

        return Web3.Convert.FromWei(balance.Value, Decimals);
    }

    public async Task<string> TransferEthAsync(string to, decimal amountEth, CancellationToken cancellationToken = default)
    {
        var receipt = await _web3Factory.Client.Eth
            .GetEtherTransferService()
            .TransferEtherAndWaitForReceiptAsync(to, amountEth)
            .WaitAsync(_timeout, cancellationToken);

        if (receipt.Status?.Value != 1)
        {
            throw new InvalidOperationException($"Переказ {amountEth} ETH на {to} відхилено мережею (status = 0).");
        }

        return receipt.TransactionHash;
    }
}