namespace DeFi.Models;

public sealed record DeploymentState(
    long ChainId,
    string DeployerAddress,
    string? ReceiverAddress,
    string? DestinationRouterAddress,
    string? MessengerAddress,
    string? SourceRouterAddress,
    string? LinkTokenAddress,
    string? LastMessageId,
    DateTimeOffset UpdatedAtUtc)
{
    public static DeploymentState Empty(long chainId, string deployer) =>
        new(chainId, deployer, null, null, null, null, null, null, DateTimeOffset.UtcNow);

    public bool MatchesEnvironment(long chainId, string deployer) =>
        ChainId == chainId &&
        string.Equals(DeployerAddress, deployer, StringComparison.OrdinalIgnoreCase);
}