using System.Text.Json;
using DeFi.Models;
using Microsoft.Extensions.Options;

namespace DeFi.Services;

public interface IDeploymentStateStore
{
    Task<DeploymentState> LoadAsync(long chainId, string deployer, CancellationToken cancellationToken = default);
    Task SaveAsync(DeploymentState state, CancellationToken cancellationToken = default);
    string FilePath { get; }
}

public sealed class DeploymentStateStore : IDeploymentStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public DeploymentStateStore(IOptions<Web3Settings> options, IHostEnvironment environment)
    {
        var configured = options.Value.StateFilePath;

        FilePath = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(environment.ContentRootPath, configured);
    }

    public string FilePath { get; }

    public async Task<DeploymentState> LoadAsync(long chainId, string deployer, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath))
        {
            return DeploymentState.Empty(chainId, deployer);
        }

        try
        {
            await using var stream = File.OpenRead(FilePath);
            var state = await JsonSerializer.DeserializeAsync<DeploymentState>(stream, JsonOptions, cancellationToken);

            if (state is null || !state.MatchesEnvironment(chainId, deployer))
            {
                return DeploymentState.Empty(chainId, deployer);
            }

            return state;
        }
        catch (JsonException)
        {
            return DeploymentState.Empty(chainId, deployer);
        }
    }

    public async Task SaveAsync(DeploymentState state, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(FilePath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(FilePath);
        await JsonSerializer.SerializeAsync(stream, state with { UpdatedAtUtc = DateTimeOffset.UtcNow }, JsonOptions, cancellationToken);
    }
}