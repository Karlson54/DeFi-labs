using System.Text;
using Defi.Models;
using Defi.Services;
using Nethereum.JsonRpc.Client;

Console.OutputEncoding = Encoding.UTF8;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables("DEFILAB4_")
    .Build();

var services = new ServiceCollection();

services.Configure<Web3Settings>(configuration.GetSection("Web3Settings"));
services.Configure<Lab4Settings>(configuration.GetSection("Lab4Settings"));

services.AddSingleton<IContractArtifactProvider, ContractArtifactProvider>();
services.AddSingleton<IWeb3Factory, Web3Factory>();
services.AddSingleton<IDeploymentStateStore, DeploymentStateStore>();
services.AddSingleton<ITokenService, TokenService>();
services.AddSingleton<IDefiIntegratorService, DefiIntegratorService>();
services.AddSingleton<IIntegrationRunner, IntegrationRunner>();
services.AddSingleton<IReportRenderer, ConsoleReportRenderer>();

await using var provider = services.BuildServiceProvider();

try
{
    var runner = provider.GetRequiredService<IIntegrationRunner>();
    var renderer = provider.GetRequiredService<IReportRenderer>();

    Console.WriteLine("Запуск інтеграції з зовнішнім DEX-протоколом (Лаб. роб. №4)...");
    Console.WriteLine("Перший запуск розгортає токени й контракт-інтегратор — це може зайняти хвилину.");

    var report = await runner.RunAsync();

    Console.WriteLine(renderer.Render(report));
    return 0;
}
catch (HttpRequestException ex)
{
    WriteError(
        "Не вдалося підключитися до RPC-вузла.",
        "Перевірте Web3Settings:RpcUrl (наприклад, Infura/Alchemy endpoint для Sepolia " +
        "або адресу локальної ноди) та наявність інтернет-з'єднання.",
        ex.Message);
    return 1;
}
catch (RpcResponseException ex)
{
    WriteError(
        "Вузол відхилив запит.",
        "Типові причини: недостатньо тестового ETH на газ, невірна адреса Router-а " +
        "у Lab4Settings:RouterAddress, або транзакція відкотилася через require у контракті.",
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