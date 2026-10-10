using System.Text;
using DeFi.Models;
using DeFi.Services;
using Nethereum.ABI.FunctionEncoding;
using Nethereum.Contracts;
using Nethereum.JsonRpc.Client;

Console.OutputEncoding = Encoding.UTF8;

var runTrack = args.Contains("--track");

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables("DEFILAB8_")
    .Build();

var services = new ServiceCollection();

services.Configure<Web3Settings>(configuration.GetSection("Web3Settings"));
services.Configure<Lab8Settings>(configuration.GetSection("Lab8Settings"));

services.AddSingleton<IContractArtifactProvider, ContractArtifactProvider>();
services.AddSingleton<IWeb3Factory, Web3Factory>();
services.AddSingleton<IDeploymentStateStore, DeploymentStateStore>();
services.AddSingleton<ILinkService, LinkService>();
services.AddSingleton<IMessengerService, MessengerService>();
services.AddSingleton<IReceiverService, ReceiverService>();
services.AddSingleton<IDeliveryTracker, DeliveryTracker>();
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
    var renderer = provider.GetRequiredService<IReportRenderer>();

    if (runTrack)
    {
        var tracker = provider.GetRequiredService<IDeliveryTracker>();
        var delivery = await tracker.TrackAsync(cts.Token);
        Console.WriteLine(renderer.Render(delivery));
        return delivery.Delivered ? 0 : 1;
    }

    var runner = provider.GetRequiredService<IScenarioRunner>();

    Console.WriteLine("Запуск крос-чейн взаємодії L1 -> L2 через Chainlink CCIP (Лаб. роб. №8)...");
    Console.WriteLine("Перший запуск розгортає контракти у двох мережах — це може зайняти кілька хвилин.");

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
        "Перевірте Web3Settings:RpcUrl (вихідна мережа), Lab8Settings:Destination:RpcUrl (L2) та інтернет-з'єднання.",
        ex.Message);
    return 1;
}
catch (RpcResponseException ex)
{
    WriteError(
        "Вузол відхилив запит.",
        "Типові причини: недостатньо тестового ETH на газ (у L1 або L2), невірні адреси Router-а, " +
        "непідтримуваний напрямок (UnsupportedDestinationChain — перевірте chain selector) або брак LINK.",
        ex.Message);
    return 1;
}
catch (SmartContractRevertException ex)
{
    WriteError(
        "Транзакцію відкотив смарт-контракт.",
        "Перевірте LINK-баланс месенджера, chain selector та те, що викликає власник контракту.",
        ex.Message);
    return 1;
}
catch (SmartContractCustomErrorRevertException ex)
{
    WriteError(
        "Router/контракт відкотив виклик з кастомною помилкою.",
        "Перші 4 байти (8 hex-символів після 0x) — селектор помилки.",
        ex.ExceptionEncodedData ?? ex.Message);
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