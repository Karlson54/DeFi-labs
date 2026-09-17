namespace DeFi.Models;

public sealed class Web3Settings
{
    public string RpcUrl { get; set; } = "http://127.0.0.1:8545";

    public long ChainId { get; set; } = 31337;

    public string PrivateKey { get; set; } = string.Empty;

    public string ArtifactsPath { get; set; } = "contracts/artifacts";

    public string StateFilePath { get; set; } = "deployment-state.json";

    public int TransactionTimeoutSeconds { get; set; } = 180;
}
