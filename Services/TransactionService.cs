using DeFi.Models;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;

namespace DeFi.Services;

public class TransactionService : ITransactionService
{
    private readonly string _rpcUrl;
    private readonly int _chainId;

    public TransactionService(string rpcUrl, int chainId)
    {
        _rpcUrl = rpcUrl;
        _chainId = chainId;
    }

    public async Task<TransactionResult> SendEtherAndWaitForReceiptAsync(
        string senderPrivateKeyHex,
        string recipientAddress,
        decimal amountInEther)
    {
        var account = new Account(senderPrivateKeyHex, _chainId);

        var web3 = new Web3(account, _rpcUrl);

        var transferService = web3.Eth.GetEtherTransferService();
        var receipt = await transferService.TransferEtherAndWaitForReceiptAsync(
            recipientAddress,
            amountInEther);

        return new TransactionResult(
            TransactionHash: receipt.TransactionHash,
            Success: receipt.Succeeded(),
            BlockNumber: receipt.BlockNumber.Value,
            GasUsed: receipt.GasUsed.Value
        );
    }
}
