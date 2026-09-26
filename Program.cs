using System.Numerics;
using System.Text;
using System.Text.Json;
using DeFi.Data;
using DeFi.Models;
using DeFi.Services;
using Microsoft.EntityFrameworkCore;
using Nethereum.Web3;

Console.OutputEncoding = Encoding.UTF8;

var runSwapOnce = args.Contains("--swap-once");
var runDeploy = args.Contains("--deploy");

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables("DEFILAB_");

builder.Services.Configure<Web3Settings>(builder.Configuration.GetSection("Web3Settings"));
builder.Services.Configure<IndexerSettings>(builder.Configuration.GetSection("IndexerSettings"));
builder.Services.Configure<CorsSettings>(builder.Configuration.GetSection("CorsSettings"));

builder.Services.AddSingleton<IWeb3Factory, Web3Factory>();
builder.Services.AddSingleton<IDeploymentStateStore, DeploymentStateStore>();

builder.Services.AddDbContext<SwapIndexerDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("SwapIndexer")));

const string CorsPolicyName = "ReactFrontend";
var corsOrigin = builder.Configuration["CorsSettings:AllowedOrigin"] ?? "http://localhost:5173";

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
        policy.WithOrigins(corsOrigin).AllowAnyHeader().AllowAnyMethod());
});

if (!runSwapOnce && !runDeploy)
{
    builder.Services.AddHostedService<SwapIndexerService>();
}

var app = builder.Build();

if (runDeploy)
{
    await DeployOnceAsync(app.Services);
    return;
}

if (runSwapOnce)
{
    await SwapOnceAsync(app.Services, args);
    return;
}

app.UseCors(CorsPolicyName);

app.MapGet("/api/swaps", async (string? trader, SwapIndexerDbContext db) =>
{
    var query = db.SwapRecords.AsNoTracking().OrderByDescending(r => r.BlockNumber).AsQueryable();

    if (!string.IsNullOrWhiteSpace(trader))
    {
        var normalized = trader.ToLowerInvariant();
        query = query.Where(r => r.Trader.ToLower() == normalized);
    }

    var records = await query.Take(500).ToListAsync();

    return Results.Ok(records.Select(r => new
    {
        r.TransactionHash,
        r.BlockNumber,
        r.Trader,
        r.TokenIn,
        r.AmountIn,
        r.AmountOut,
        r.FeeBps,
        r.IndexedAtUtc
    }));
});

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

Console.WriteLine("Web3-індексатор та REST API запущено на http://localhost:5000.");
Console.WriteLine("Розгорнути контракти заново (в іншому терміналі): dotnet run -- --deploy");
Console.WriteLine("Демонстраційний своп (в іншому терміналі): dotnet run -- --swap-once [--amount 50]");

await app.RunAsync();

static async Task DeployOnceAsync(IServiceProvider services)
{
    var web3Factory = services.GetRequiredService<IWeb3Factory>();
    var stateStore = services.GetRequiredService<IDeploymentStateStore>();
    var web3Settings = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Web3Settings>>().Value;
    var environment = services.GetRequiredService<IHostEnvironment>();

    var deployer = web3Factory.AccountAddress;
    var chainId = web3Factory.ChainId;
    var web3 = web3Factory.Client;

    var artifactsRoot = Path.IsPathRooted(web3Settings.ArtifactsPath)
        ? web3Settings.ArtifactsPath
        : Path.Combine(environment.ContentRootPath, web3Settings.ArtifactsPath);

    var (tokenAbi, tokenBytecode) = LoadArtifact(artifactsRoot, "AssetToken");
    var (poolAbi, poolBytecode) = LoadArtifact(artifactsRoot, "DexPool");

    Console.WriteLine("1/5 Деплой токена A (RUBC)...");
    var tokenAAddress = await DeployContractAsync(
        web3, deployer, tokenAbi, tokenBytecode,
        "Ruban Andrii Coin", "RUBC", Web3.Convert.ToWei(1_000_000m, 18));
    Console.WriteLine($"   TokenA: {tokenAAddress}");

    Console.WriteLine("2/5 Деплой токена B (MFIAT)...");
    var tokenBAddress = await DeployContractAsync(
        web3, deployer, tokenAbi, tokenBytecode,
        "Mock Fiat", "MFIAT", Web3.Convert.ToWei(1_000_000m, 18));
    Console.WriteLine($"   TokenB: {tokenBAddress}");

    Console.WriteLine("3/5 Деплой DexPool...");
    var poolAddress = await DeployContractAsync(
        web3, deployer, poolAbi, poolBytecode,
        tokenAAddress, tokenBAddress);
    Console.WriteLine($"   Pool: {poolAddress}");

    Console.WriteLine("4/5 Approve для ліквідності...");
    await ApproveAsync(web3, deployer, tokenAbi, tokenAAddress, poolAddress, Web3.Convert.ToWei(1000m, 18));
    await ApproveAsync(web3, deployer, tokenAbi, tokenBAddress, poolAddress, Web3.Convert.ToWei(2000m, 18));

    Console.WriteLine("5/5 addLiquidity(1000, 2000)...");
    var poolContract = web3.Eth.GetContract(poolAbi, poolAddress);
    var addLiquidityFunction = poolContract.GetFunction("addLiquidity");
    var addLiquidityGas = await addLiquidityFunction.EstimateGasAsync(
        deployer, null, null, Web3.Convert.ToWei(1000m, 18), Web3.Convert.ToWei(2000m, 18));
    var addLiquidityReceipt = await addLiquidityFunction.SendTransactionAndWaitForReceiptAsync(
        deployer, addLiquidityGas, null, null, Web3.Convert.ToWei(1000m, 18), Web3.Convert.ToWei(2000m, 18));
    Console.WriteLine($"   tx: {addLiquidityReceipt.TransactionHash}, status: {addLiquidityReceipt.Status?.Value}");

    var newState = new DeploymentState(chainId, deployer, tokenAAddress, tokenBAddress, poolAddress,
        DateTimeOffset.UtcNow);
    await stateStore.SaveAsync(newState);

    Console.WriteLine($"Готово. deployment-state.json оновлено: {stateStore.FilePath}");
}

static async Task SwapOnceAsync(IServiceProvider services, string[] args)
{
    var web3Factory = services.GetRequiredService<IWeb3Factory>();
    var stateStore = services.GetRequiredService<IDeploymentStateStore>();

    var deployer = web3Factory.AccountAddress;
    var chainId = web3Factory.ChainId;

    var state = await stateStore.LoadAsync(chainId, deployer);

    if (string.IsNullOrWhiteSpace(state.TokenAAddress) || string.IsNullOrWhiteSpace(state.PoolAddress))
    {
        Console.Error.WriteLine(
            "[ПОМИЛКА] Пул ще не розгорнутий. Спочатку виконайте: dotnet run -- --deploy");
        return;
    }

    var amount = 50m;
    var amountArgIndex = Array.IndexOf(args, "--amount");
    if (amountArgIndex >= 0 && amountArgIndex + 1 < args.Length &&
        decimal.TryParse(args[amountArgIndex + 1], out var parsedAmount))
    {
        amount = parsedAmount;
    }

    var amountInWei = Web3.Convert.ToWei(amount, 18);
    var web3 = web3Factory.Client;

    const string approveAbi =
        "[{\"inputs\":[{\"internalType\":\"address\",\"name\":\"spender\",\"type\":\"address\"},{\"internalType\":\"uint256\",\"name\":\"amount\",\"type\":\"uint256\"}],\"name\":\"approve\",\"outputs\":[{\"internalType\":\"bool\",\"name\":\"\",\"type\":\"bool\"}],\"stateMutability\":\"nonpayable\",\"type\":\"function\"}]";
    const string swapAbi =
        "[{\"inputs\":[{\"internalType\":\"uint256\",\"name\":\"amountIn\",\"type\":\"uint256\"},{\"internalType\":\"uint256\",\"name\":\"minAmountOut\",\"type\":\"uint256\"}],\"name\":\"swapAForB\",\"outputs\":[{\"internalType\":\"uint256\",\"name\":\"\",\"type\":\"uint256\"}],\"stateMutability\":\"nonpayable\",\"type\":\"function\"}]";

    Console.WriteLine($"Своп {amount} токенів A -> B на пулі {state.PoolAddress}...");

    Console.WriteLine("1/2 Approve...");
    await ApproveAsync(web3, deployer, approveAbi, state.TokenAAddress!, state.PoolAddress!, amountInWei);

    Console.WriteLine("2/2 Swap...");
    var poolContract = web3.Eth.GetContract(swapAbi, state.PoolAddress);
    var swapFunction = poolContract.GetFunction("swapAForB");
    var swapGas = await swapFunction.EstimateGasAsync(deployer, null, null, amountInWei, BigInteger.Zero);
    var swapReceipt = await swapFunction.SendTransactionAndWaitForReceiptAsync(
        deployer, swapGas, null, null, amountInWei, BigInteger.Zero);
    Console.WriteLine($"   tx: {swapReceipt.TransactionHash}, status: {swapReceipt.Status?.Value}");

    Console.WriteLine("Готово. Індексатор підхопить подію Swap протягом кількох секунд.");
}

static async Task<string> DeployContractAsync(Web3 web3, string from, string abi, string bytecode,
    params object[] constructorArgs)
{
    var gas = await web3.Eth.DeployContract.EstimateGasAsync(abi, bytecode, from, constructorArgs);
    var receipt = await web3.Eth.DeployContract.SendRequestAndWaitForReceiptAsync(
        abi, bytecode, from, gas, (System.Threading.CancellationTokenSource?)null, constructorArgs);

    if (receipt.Status?.Value != 1)
    {
        throw new InvalidOperationException($"Деплой контракту відхилено мережею (tx: {receipt.TransactionHash}).");
    }

    return receipt.ContractAddress;
}

static async Task ApproveAsync(Web3 web3, string from, string tokenAbi, string tokenAddress, string spender,
    BigInteger amount)
{
    var tokenContract = web3.Eth.GetContract(tokenAbi, tokenAddress);
    var approveFunction = tokenContract.GetFunction("approve");
    var gas = await approveFunction.EstimateGasAsync(from, null, null, spender, amount);
    var receipt = await approveFunction.SendTransactionAndWaitForReceiptAsync(from, gas, null, null, spender, amount);

    if (receipt.Status?.Value != 1)
    {
        throw new InvalidOperationException($"Approve відхилено мережею (tx: {receipt.TransactionHash}).");
    }
}

static (string Abi, string Bytecode) LoadArtifact(string artifactsRoot, string contractName)
{
    var path = Path.Combine(artifactsRoot, $"{contractName}.json");

    if (!File.Exists(path))
    {
        throw new InvalidOperationException(
            $"Не знайдено артефакт '{contractName}' за шляхом {path}. " +
            "Скомпілюйте .sol у Remix і збережіть abi+bytecode у цей файл (див. README, Крок 1).");
    }

    using var document = JsonDocument.Parse(File.ReadAllText(path));
    var root = document.RootElement;

    var abi = root.GetProperty("abi").GetRawText();

    var bytecodeValue =
        root.TryGetProperty("bytecode", out var bytecode) ? bytecode.GetString() :
        root.TryGetProperty("bin", out var bin) ? bin.GetString() :
        null;

    if (string.IsNullOrWhiteSpace(bytecodeValue) || bytecodeValue is "0x")
    {
        throw new InvalidOperationException($"У артефакті '{contractName}' відсутній байткод.");
    }

    var normalizedBytecode = bytecodeValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        ? bytecodeValue
        : "0x" + bytecodeValue;

    return (abi, normalizedBytecode);
}