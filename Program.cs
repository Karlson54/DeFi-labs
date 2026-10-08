using System.Text;
using DeFi.Models;
using DeFi.Services;
using Nethereum.JsonRpc.Client;

Console.OutputEncoding = Encoding.UTF8;

//   dotnet run               -> повний життєвий цикл інвестора (контрольне завдання)
//   dotnet run -- --stand    -> підготовка стенду: деплой, пул, депозит (без compound)
//   dotnet run -- --bot      -> автономний бот-кіпер (викликає compound)
//   dotnet run -- --donate   -> імітація фарму: переказ винагороди на сховище
var runBot = args.Contains("--bot");
var runStand = args.Contains("--stand");
var runDonate = args.Contains("--donate");

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables("DEFILAB7_")
    .Build();

var services = new ServiceCollection();

services.Configure<Web3Settings>(configuration.GetSection("Web3Settings"));
services.Configure<Lab7Settings>(configuration.GetSection("Lab7Settings"));

services.AddSingleton<IContractArtifactProvider, ContractArtifactProvider>();
services.AddSingleton<IWeb3Factory, Web3Factory>();
services.AddSingleton<IDeploymentStateStore, DeploymentStateStore>();
services.AddSingleton<ITokenService, TokenService>();
services.AddSingleton<IRouterService, RouterService>();
services.AddSingleton<IVaultService, VaultService>();
services.AddSingleton<IKeeperBot, KeeperBot>();
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
        var bot = provider.GetRequiredService<IKeeperBot>();
        await bot.RunAsync(cts.Token);
        return 0;
    }

    var runner = provider.GetRequiredService<IScenarioRunner>();
    var renderer = provider.GetRequiredService<IReportRenderer>();

    if (runDonate)
    {
        Console.WriteLine("Імітація фарму: переказ винагороди на сховище (Лаб. роб. №7)...");
        var donation = await runner.DonateRewardAsync(cts.Token);
        Console.WriteLine(renderer.Render(donation));
        return 0;
    }

    if (runStand)
    {
        Console.WriteLine("Підготовка стенду: токени, пул у зовнішньому DEX, сховище, депозит (Лаб. роб. №7)...");
        var stand = await runner.PrepareStandAsync(cts.Token);
        Console.WriteLine(renderer.Render(stand));
        return 0;
    }

    Console.WriteLine("Запуск життєвого циклу інвестора у сховищі з автокомпаундингом (Лаб. роб. №7)...");
    Console.WriteLine("Перший запуск розгортає токени, наповнює пул і розгортає сховище — це може зайняти кілька хвилин.");

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
        "Перевірте Web3Settings:RpcUrl (Infura/Alchemy endpoint для Sepolia або адреса локальної ноди) та інтернет-з'єднання.",
        ex.Message);
    return 1;
}
catch (RpcResponseException ex)
{
    WriteError(
        "Вузол відхилив запит.",
        "Типові причини: недостатньо тестового ETH на газ, невірна адреса Router-а у Lab7Settings:RouterAddress, " +
        "відсутній пул B->A або відкат через require у контракті.",
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