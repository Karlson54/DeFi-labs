using System.Numerics;

namespace DeFi.Models;

public record TransactionResult(
    string TransactionHash,
    bool Success,
    BigInteger BlockNumber,
    BigInteger GasUsed
);
