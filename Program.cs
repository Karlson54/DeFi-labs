using System.Text;
using DeFi.Models;
using DeFi.Services;
using Nethereum.JsonRpc.Client;

Console.OutputEncoding = Encoding.UTF8;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables("DEFILAB5_")
    .Build();

var services = new ServiceCollection();

services.Configure<Web3Settings>(configuration.GetSection("Web3Settings"));
services.Configure<Lab5Settings>(configuration.GetSection("Lab5Settings"));

services.AddSingleton<IContractArtifactProvider, ContractArtifactProvider>();
services.AddSingleton<IWeb3Factory, Web3Factory>();
services.AddSingleton<IDeploymentStateStore, DeploymentStateStore>();
services.AddSingleton<IStablecoinService, StablecoinService>();
services.AddSingleton<IStableEngineService, StableEngineService>();
services.AddSingleton<IScenarioRunner, ScenarioRunner>();
services.AddSingleton<IReportRenderer, ConsoleReportRenderer>();

await using var provider = services.BuildServiceProvider();

try
{
    var runner = provider.GetRequiredService<IScenarioRunner>();
    var renderer = provider.GetRequiredService<IReportRenderer>();

    Console.WriteLine("Запуск моделювання кредитного протоколу зі стейблкоїном (Лаб. роб. №5)...");
    Console.WriteLine("Перший запуск розгортає стейблкоїн і кредитне ядро — це може зайняти хвилину.");

    var report = await runner.RunAsync();

    Console.WriteLine(renderer.Render(report));
    return 0;
}
catch (HttpRequestException ex)
{
    WriteError(
        "Не вдалося підключитися до RPC-вузла.",
        "Перевірте Web3Settings:RpcUrl та чи запущена локальна нода (npx hardhat node).",
        ex.Message);
    return 1;
}
catch (RpcResponseException ex)
{
    WriteError(
        "Вузол відхилив запит.",
        "Типові причини: недостатньо ETH на газ чи на заставу, або транзакція відкотилася через require у контракті.",
        ex.Message);
    return 1;
}
catch (TimeoutException ex)
{
    WriteError(
        "Транзакція не потрапила в блок за відведений час.",
        "Збільште Web3Settings:TransactionTimeoutSeconds або перевірте, чи майнить нода блоки.",
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