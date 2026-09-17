namespace DeFi.Models;

public sealed record DeploymentState(
    long ChainId,
    string DeployerAddress,
    string? TokenAAddress,
    string? TokenBAddress,
    string? PoolAddress,
    DateTimeOffset UpdatedAtUtc)
{
    public static DeploymentState Empty(long chainId, string deployer) =>
        new(chainId, deployer, null, null, null, DateTimeOffset.UtcNow);

    public bool MatchesEnvironment(long chainId, string deployer) =>
        ChainId == chainId &&
        string.Equals(DeployerAddress, deployer, StringComparison.OrdinalIgnoreCase);

    public bool HasFullDeployment =>
        !string.IsNullOrWhiteSpace(TokenAAddress) &&
        !string.IsNullOrWhiteSpace(TokenBAddress) &&
        !string.IsNullOrWhiteSpace(PoolAddress);
}
