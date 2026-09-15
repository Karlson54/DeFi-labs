using System.Text.Json;
using Lab1.Models;

namespace Lab1.Services;

public class WalletStorageService : IWalletStorageService
{
    private readonly string _filePath;

    public WalletStorageService(string filePath = "wallet.json")
    {
        _filePath = filePath;
    }

    public WalletInfo? TryLoad()
    {
        if (!File.Exists(_filePath))
            return null;

        var json = File.ReadAllText(_filePath);
        var saved = JsonSerializer.Deserialize<SavedWallet>(json);

        if (saved is null)
            return null;

        return new WalletInfo(saved.PrivateKeyHex, saved.Address, Attempts: 0, Elapsed: TimeSpan.Zero);
    }

    public void Save(WalletInfo wallet)
    {
        var saved = new SavedWallet(wallet.PrivateKeyHex, wallet.Address);
        var json = JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }

    private record SavedWallet(string PrivateKeyHex, string Address);
}
