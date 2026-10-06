namespace DeFi.Models;

public sealed record DeploymentState(
    long ChainId,
    string DeployerAddress,
    string? StablecoinAddress,
    string? EngineAddress,
    DateTimeOffset UpdatedAtUtc)
{
    public static DeploymentState Empty(long chainId, string deployer) =>
        new(chainId, deployer, null, null, DateTimeOffset.UtcNow);

    public bool MatchesEnvironment(long chainId, string deployer) =>
        ChainId == chainId &&
        string.Equals(DeployerAddress, deployer, StringComparison.OrdinalIgnoreCase);
}