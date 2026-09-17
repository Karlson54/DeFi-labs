using System.Text.Json;
using DeFi.Models;
using Microsoft.Extensions.Options;

namespace DeFi.Services;

public interface IContractArtifactProvider
{
    Task<ContractArtifact> GetAsync(string contractName, CancellationToken cancellationToken = default);
}

public sealed class ContractArtifactProvider : IContractArtifactProvider
{
    private readonly string _artifactsRoot;

    private readonly Dictionary<string, ContractArtifact> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ContractArtifactProvider(IOptions<Web3Settings> options)
    {
        var configured = options.Value.ArtifactsPath;

        _artifactsRoot = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, configured);
    }

    public async Task<ContractArtifact> GetAsync(string contractName, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(contractName, out var cached))
        {
            return cached;
        }

        var path = Path.Combine(_artifactsRoot, $"{contractName}.json");

        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Не знайдено артефакт контракту '{contractName}' за шляхом {path}. " +
                "Скомпілюйте .sol-файли (Remix / solc / hardhat) і збережіть abi + bytecode у цей файл. " +
                "Детальна інструкція — у розділі README «Крок 1. Компіляція контрактів».");
        }

        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        var abi = ExtractAbi(root, contractName);
        var bytecode = ExtractBytecode(root, contractName);

        var artifact = new ContractArtifact(contractName, abi, bytecode);
        _cache[contractName] = artifact;
        return artifact;
    }

    private static string ExtractAbi(JsonElement root, string contractName)
    {
        if (!root.TryGetProperty("abi", out var abi) || abi.ValueKind != JsonValueKind.Array || abi.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(
                $"У артефакті контракту '{contractName}' відсутній або порожній масив 'abi'. " +
                "Схоже, у файл вставлено заготовку — замініть її результатом компіляції.");
        }

        return abi.GetRawText();
    }

    private static string ExtractBytecode(JsonElement root, string contractName)
    {
        var value =
            root.TryGetProperty("bytecode", out var bytecode) ? bytecode.GetString() :
            root.TryGetProperty("bin", out var bin) ? bin.GetString() :
            null;

        if (string.IsNullOrWhiteSpace(value) || value is "0x")
        {
            throw new InvalidOperationException(
                $"У артефакті контракту '{contractName}' відсутній байткод. " +
                "Скопіюйте поле 'object' з Remix (Compilation Details -> BYTECODE) у ключ 'bytecode'.");
        }

        return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value : "0x" + value;
    }
}
