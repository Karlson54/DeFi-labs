using System.Numerics;

namespace Lab1.Models;

public record TransactionResult(
    string TransactionHash,
    bool Success,
    BigInteger BlockNumber,
    BigInteger GasUsed
);
