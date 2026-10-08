using System.Text;
using DeFi.Models;
using DeFi.Services;
using Nethereum.JsonRpc.Client;

Console.OutputEncoding = Encoding.UTF8;

//   dotnet run                -> підготовка стенду (деплой, позиція з HF ≈ 1.1, фінансування ліквідатора)
//   dotnet run -- --bot       -> автономний бот-ліквідатор
//   dotnet run -- --crash     -> бекдор: штучна неплатоспроможність позичальника
var runBot = args.Contains("--bot");
var runCrash = args.Contains("--crash");

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables("DEFILAB6_")
    .Build();

var services = new ServiceCollection();

services.Configure<Web3Settings>(configuration.GetSection("Web3Settings"));
services.Configure<Lab6Settings>(configuration.GetSection("Lab6Settings"));

services.AddSingleton<IContractArtifactProvider, ContractArtifactProvider>();
services.AddSingleton<IWeb3Factory, Web3Factory>();
services.AddSingleton<IDeploymentStateStore, DeploymentStateStore>();
services.AddSingleton<IWalletService, WalletService>();
services.AddSingleton<IStablecoinService, StablecoinService>();
services.AddSingleton<IStableEngineService, StableEngineService>();
services.AddSingleton<ILiquidatorService, LiquidatorService>();
services.AddSingleton<ILiquidatorBot, LiquidatorBot>();
services.AddSingleton<IScenarioRunner, ScenarioRunner>();
services.AddSingleton<IReportRenderer, ConsoleReportRenderer>();

await using var provider = services.BuildServiceProvider();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    if (runBot)
    {
        var bot = provider.GetRequiredService<ILiquidatorBot>();
        await bot.RunAsync(cts.Token);
        return 0;
    }

    var runner = provider.GetRequiredService<IScenarioRunner>();
    var renderer = provider.GetRequiredService<IReportRenderer>();

    if (runCrash)
    {
        Console.WriteLine("Лабораторний бекдор: штучне зменшення застави позичальника (Лаб. роб. №6)...");

        var crash = await runner.SimulateCrashAsync(cts.Token);
        Console.WriteLine(renderer.Render(crash));
        return 0;
    }

    Console.WriteLine("Підготовка стенду: кредитний протокол + оракул Chainlink + ліквідатор (Лаб. роб. №6)...");
    Console.WriteLine("Перший запуск розгортає контракти й виконує кілька транзакцій у Sepolia — це може зайняти кілька хвилин.");

    var report = await runner.RunAsync(cts.Token);
    Console.WriteLine(renderer.Render(report));
    return 0;
}
catch (OperationCanceledException)
{
    Console.WriteLine("Зупинено користувачем.");
    return 0;
}
catch (HttpRequestException ex)
{
    WriteError(
        "Не вдалося підключитися до RPC-вузла.",
        "Перевірте Web3Settings:RpcUrl (Infura/Alchemy endpoint для Sepolia) та інтернет-з'єднання.",
        ex.Message);
    return 1;
}
catch (RpcResponseException ex)
{
    WriteError(
        "Вузол відхилив запит.",
        "Типові причини: недостатньо тестового ETH на газ чи заставу, невірна адреса оракула, " +
        "застаріла ціна оракула (stale oracle price) або відкат через require у контракті.",
        ex.Message);
    return 1;
}
catch (TimeoutException ex)
{
    WriteError(
        "Транзакція не потрапила в блок за відведений час.",
        "Збільште Web3Settings:TransactionTimeoutSeconds або перевірте завантаженість мережі.",
        ex.Message);
    return 1;
}
catch (InvalidOperationException ex)
{
    WriteError("Помилка конфігурації або даних контракту.", null, ex.Message);
    return 1;
}

static void WriteError(string title, string? hint, string details)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"[ПОМИЛКА] {title}");

    if (!string.IsNullOrWhiteSpace(hint))
    {
        Console.Error.WriteLine(hint);
    }

    Console.Error.WriteLine($"Деталі: {details}");
    Console.Error.WriteLine();
}