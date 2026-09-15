using Lab1.Models;

namespace Lab1.Services;

public interface IWalletService
{
    WalletInfo GenerateWalletWithAddressPrefix(string hexPrefix, IProgress<long>? progress = null);
}
