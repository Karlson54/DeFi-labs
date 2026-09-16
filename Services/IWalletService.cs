using DeFi.Models;

namespace DeFi.Services;

public interface IWalletService
{
    WalletInfo GenerateWalletWithAddressPrefix(string hexPrefix, IProgress<long>? progress = null);
}
