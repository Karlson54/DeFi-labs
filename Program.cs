using System.Text;
using DeFi.Models;
using DeFi.Services;
using Nethereum.ABI.FunctionEncoding;
using Nethereum.Contracts;
using Nethereum.JsonRpc.Client;

Console.OutputEncoding = Encoding.UTF8;

var runAttack = args.Contains("--attack");

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables("DEFILAB9_")
    .Build();

var services = new ServiceCollection();

services.Configure<Web3Settings>(configuration.GetSection("Web3Settings"));
services.Configure<Lab9Settings>(configuration.GetSection("Lab9Settings"));

services.AddSingleton<IContractArtifactProvider, ContractArtifactProvider>();
services.AddSingleton<IWeb3Factory, Web3Factory>();
services.AddSingleton<IDeploymentStateStore, DeploymentStateStore>();
services.AddSingleton<IContractDeployer, ContractDeployer>();
services.AddSingleton<ITokenService, TokenService>();
services.AddSingleton<IProtocolService, ProtocolService>();
services.AddSingleton<IDaoService, DaoService>();
services.AddSingleton<IFlashLoanService, FlashLoanService>();
services.AddSingleton<IScenarioRunner, ScenarioRunner>();
services.AddSingleton<IAttackRunner, AttackRunner>();
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

    if (runAttack)
    {
        Console.WriteLine("Симуляція Flash Loan-атаки на вразливий ціновий оракул (Лаб. роб. №9)...");
        Console.WriteLine("Розгортається чистий стенд: токени, пул, оракули, два кредитори, атакуючі.");

        var attack = await provider.GetRequiredService<IAttackRunner>().RunAsync(cts.Token);
        Console.WriteLine(renderer.Render(attack));
        return 0;
    }

    Console.WriteLine("Запуск децентралізованого управління: DAO змінює параметр протоколу (Лаб. роб. №9)...");
    Console.WriteLine("Скрипт чекає завершення голосування (SimpleDAO.VOTING_PERIOD) — це близько хвилини.");

    var report = await provider.GetRequiredService<IScenarioRunner>().RunAsync(cts.Token);
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
        "Перевірте Web3Settings:RpcUrl та чи запущена локальна нода (npx hardhat node).",
        ex.Message);
    return 1;
}
catch (RpcResponseException ex)
{
    WriteError(
        "Вузол відхилив запит.",
        "Типові причини: недостатньо ETH на газ, відкат через require у контракті " +
        "або застарілий файл стану після перезапуску ноди (видаліть deployment-state-lab9.json).",
        ex.Message);
    return 1;
}
catch (SmartContractRevertException ex)
{
    WriteError("Транзакцію відкотив смарт-контракт.", "Перевірте причину відкату нижче.", ex.Message);
    return 1;
}
catch (SmartContractCustomErrorRevertException ex)
{
    WriteError(
        "Контракт відкотив виклик з кастомною помилкою.",
        "Перші 4 байти (8 hex-символів після 0x) — селектор помилки.",
        ex.ExceptionEncodedData ?? ex.Message);
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