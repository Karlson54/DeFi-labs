# Лабораторна робота №1 — Рубан Андрій ПДМ-61

Тема: Інфраструктура гаманців та криптографія (стек: C# / .NET 8 / Nethereum)

## Структура проєкту

```
RubanWeb3Lab/
├── Program.cs                  # точка входу, DI, сценарій виконання
├── appsettings.json             # RPC URL, день народження, сума переказу
├── Models/
│   ├── WalletInfo.cs            # DTO результату генерації гаманця
│   └── TransactionResult.cs     # DTO результату транзакції
└── Services/
    ├── IWalletService.cs / WalletService.cs           # генерація ключів + PoW
    ├── IBalanceService.cs / BalanceService.cs         # читання балансу
    └── ITransactionService.cs / TransactionService.cs # підпис і відправка
```

## Як запустити

1. Встановити .NET 8 SDK (якщо ще не встановлено).
2. Зареєструватися на [alchemy.com](https://www.alchemy.com/), створити застосунок на мережу **Ethereum Sepolia**, скопіювати HTTPS RPC URL.
3. Відкрити `appsettings.json` і вставити URL у поле `RpcUrl`:
   ```json
   "RpcUrl": "https://eth-sepolia.g.alchemy.com/v2/ВАШ_КЛЮЧ"
   ```
4. За потреби змінити `RecipientAddress` (адреса, на яку відправляти тестову транзакцію) та `AmountToSendEther`.
5. У корені проєкту виконати:
   ```bash
   dotnet restore
   dotnet run
   ```
6. Програма згенерує гаманець з адресою, що починається на `0x04` (день народження — 4 число), виведе приватний ключ і адресу.
7. Скопіювати адресу, поповнити її через [Sepolia Faucet](https://www.alchemy.com/faucets/ethereum-sepolia) і натиснути Enter в консолі.
8. Програма перевірить баланс, підпише і відправить тестову транзакцію, дочекається квитанції (receipt) і виведе хеш транзакції, номер блоку та витрачений газ.

## Чому саме такі рішення в коді

- **DI-контейнер** (`Microsoft.Extensions.DependencyInjection`) — той самий підхід, що і в ASP.NET: сервіси реєструються один раз, а не створюються вручну по всьому коду.
- **`appsettings.json`** — RPC URL і параметри винесені з коду, як connection string в EF-проєктах. Приватний ключ у конфіг **не** заноситься — він існує лише в оперативній пам'яті під час виконання.
- **Розділення на сервіси** — `WalletService` (чиста криптографія, мережа не потрібна), `BalanceService` (тільки читання), `TransactionService` (підпис + запис) — кожен відповідає за одну відповідальність (SRP).
- **DTO** (`WalletInfo`, `TransactionResult`) — щоб не тягнути "сирі" об'єкти Nethereum по коду і мати зручний формат для виводу/логування.