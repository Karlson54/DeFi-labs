namespace DeFi.Models;

public static class StateKeys
{
    public const string Token = "Token";
    public const string Protocol = "Protocol";
    public const string Dao = "Dao";
}

public sealed record DeploymentState(
    long ChainId,
    string DeployerAddress,
    Dictionary<string, string> Addresses,
    DateTimeOffset UpdatedAtUtc)
{
    public static DeploymentState Empty(long chainId, string deployer) =>
        new(chainId, deployer, new Dictionary<string, string>(), DateTimeOffset.UtcNow);

    public bool MatchesEnvironment(long chainId, string deployer) =>
        ChainId == chainId &&
        string.Equals(DeployerAddress, deployer, StringComparison.OrdinalIgnoreCase);

    public string? Get(string key) =>
        Addresses.TryGetValue(key, out var value) ? value : null;

    public DeploymentState With(string key, string address) =>
        this with { Addresses = new Dictionary<string, string>(Addresses) { [key] = address } };
}