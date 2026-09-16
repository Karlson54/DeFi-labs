using DeFi.Models;

namespace DeFi.Services;

public interface IWalletStorageService
{
    WalletInfo? TryLoad();
    void Save(WalletInfo wallet);
}
