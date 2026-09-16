using DeFi.Models;

namespace DeFi.Services;

public interface ITransactionService
{
    Task<TransactionResult> SendEtherAndWaitForReceiptAsync(
        string senderPrivateKeyHex,
        string recipientAddress,
        decimal amountInEther);
}
