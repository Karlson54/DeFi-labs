using Lab1.Models;

namespace Lab1.Services;

public interface ITransactionService
{
    Task<TransactionResult> SendEtherAndWaitForReceiptAsync(
        string senderPrivateKeyHex,
        string recipientAddress,
        decimal amountInEther);
}
