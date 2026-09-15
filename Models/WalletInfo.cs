namespace Lab1.Models;

public record WalletInfo(
    string PrivateKeyHex,
    string Address,
    long Attempts,
    TimeSpan Elapsed
);
