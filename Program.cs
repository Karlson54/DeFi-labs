using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nethereum.Web3;
using Lab1.Services;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .Build();

var rpcUrl = configuration["Web3Settings:RpcUrl"]
    ?? throw new InvalidOperationException("Не задано RpcUrl в appsettings.json");
var chainId = int.Parse(configuration["Web3Settings:ChainId"] ?? "11155111");
var birthDayDigits = configuration["Web3Settings:BirthDayDigits"] ?? "00";
var recipientAddress = configuration["Web3Settings:RecipientAddress"]
    ?? throw new InvalidOperationException("Не задано RecipientAddress в appsettings.json");
var amountToSend = decimal.Parse(
    configuration["Web3Settings:AmountToSendEther"] ?? "0.0001",
    System.Globalization.CultureInfo.InvariantCulture);

// DI1
var services = new ServiceCollection();

services.AddSingleton<IWalletService, WalletService>();
services.AddSingleton<IWalletStorageService>(_ => new WalletStorageService());
services.AddSingleton<IWeb3>(_ => new Web3(rpcUrl));
services.AddSingleton<IBalanceService, BalanceService>();
services.AddSingleton<ITransactionService>(_ => new TransactionService(rpcUrl, chainId));

var provider = services.BuildServiceProvider();

Console.WriteLine("=== Крок 1. Генерація гаманця ===");

var walletStorageService = provider.GetRequiredService<IWalletStorageService>();
var wallet = walletStorageService.TryLoad();

if (wallet is not null)
{
    Console.WriteLine("Знайдено збережений гаманець з попереднього запуску (wallet.json).");
    Console.WriteLine($"Адреса: {wallet.Address}");
}
else
{
    Console.WriteLine($"Шукаємо адресу, що починається з 0x{birthDayDigits}...");

    var walletService = provider.GetRequiredService<IWalletService>();
    var progress = new Progress<long>(attempts =>
        Console.WriteLine($"  ... спроба №{attempts:N0}"));

    wallet = walletService.GenerateWalletWithAddressPrefix(birthDayDigits, progress);
    walletStorageService.Save(wallet);

    Console.WriteLine();
    Console.WriteLine($"Знайдено за {wallet.Attempts:N0} спроб, {wallet.Elapsed.TotalSeconds:F2} c.");
    Console.WriteLine($"Адреса:        {wallet.Address}");

    Console.WriteLine($"Приватний ключ: {wallet.PrivateKeyHex}");
    Console.WriteLine();
    Console.WriteLine("Гаманець збережено у wallet.json — при повторному запуску буде використано той самий.");
}

Console.WriteLine();
Console.WriteLine("Поповніть цю адресу через Sepolia Faucet (якщо ще не зробили), а потім натисніть Enter...");
Console.ReadLine();

Console.WriteLine("=== Крок 2. Перевірка балансу ===");
var balanceService = provider.GetRequiredService<IBalanceService>();
var balance = await balanceService.GetBalanceInEtherAsync(wallet.Address);
Console.WriteLine($"Баланс {wallet.Address}: {balance} ETH");
Console.WriteLine();

if (balance <= amountToSend)
{
    Console.WriteLine("Недостатньо коштів для відправки тестової транзакції. Поповніть баланс і перезапустіть цей крок.");
    return;
}

Console.WriteLine("=== Крок 3. Відправка транзакції ===");
Console.WriteLine($"Відправляємо {amountToSend} ETH на {recipientAddress}...");

var transactionService = provider.GetRequiredService<ITransactionService>();
var result = await transactionService.SendEtherAndWaitForReceiptAsync(
    wallet.PrivateKeyHex,
    recipientAddress,
    amountToSend);

Console.WriteLine();
Console.WriteLine("=== Крок 4. Результат ===");
Console.WriteLine($"Хеш транзакції: {result.TransactionHash}");
Console.WriteLine($"Статус:         {(result.Success ? "Успішно" : "Помилка")}");
Console.WriteLine($"Номер блоку:    {result.BlockNumber}");
Console.WriteLine($"Витрачено газу: {result.GasUsed}");
