using System.Diagnostics;
using DeFi.Models;
using Nethereum.Signer;

namespace DeFi.Services;

public class WalletService : IWalletService
{
    public WalletInfo GenerateWalletWithAddressPrefix(string hexPrefix, IProgress<long>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(hexPrefix))
            throw new ArgumentException("Префікс не може бути порожнім.", nameof(hexPrefix));

        var normalizedPrefix = "0x" + hexPrefix.Trim().ToLowerInvariant();

        var stopwatch = Stopwatch.StartNew();
        long attempts = 0;

        while (true)
        {
            attempts++;

            var ecKey = EthECKey.GenerateKey();

            var address = ecKey.GetPublicAddress();

            if (progress is not null && attempts % 20_000 == 0)
                progress.Report(attempts);

            if (address.ToLowerInvariant().StartsWith(normalizedPrefix, StringComparison.Ordinal))
            {
                stopwatch.Stop();
                return new WalletInfo(
                    PrivateKeyHex: ecKey.GetPrivateKey(),
                    Address: address,
                    Attempts: attempts,
                    Elapsed: stopwatch.Elapsed
                );
            }
        }
    }
}
