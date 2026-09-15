using Lab1.Models;

namespace Lab1.Services;

public interface IWalletStorageService
{
    WalletInfo? TryLoad();
    void Save(WalletInfo wallet);
}
