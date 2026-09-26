namespace Defi.Models;

public sealed record DeploymentState(
    long ChainId,
    string DeployerAddress,
    string? TokenAAddress,
    string? TokenBAddress,
    string? IntegratorAddress,
    DateTimeOffset UpdatedAtUtc)
{
    public static DeploymentState Empty(long chainId, string deployer) =>
        new(chainId, deployer, null, null, null, DateTimeOffset.UtcNow);

    public bool MatchesEnvironment(long chainId, string deployer) =>
        ChainId == chainId &&
        string.Equals(DeployerAddress, deployer, StringComparison.OrdinalIgnoreCase);
}