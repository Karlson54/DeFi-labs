# Лабораторна робота №4 — Композитність: інтеграція з Uniswap V2 Router

**Тема.** Інтеграція смарт-контрактів з існуючими протоколами децентралізованого
обміну на рівні програмних інтерфейсів (Composability).

Клієнт — консольний застосунок C# (.NET 8) + Nethereum, повністю самодостатній:
сам випускає два власні ERC-20 токени, сам розгортає контракт-посередник
`DefiIntegrator` і через нього викликає **чужий** Router-контракт (Uniswap V2
або сумісний форк) для створення пулу ліквідності та обміну.

## Запуск

### Крок 0. Тестова мережа з розгорнутим Uniswap V2

Найпростіше — публічний форк Uniswap V2 у Sepolia (адреса Router-а видається
викладачем/береться з документації обраного форку). Альтернатива — локальний
Hardhat-форк мережі, де Uniswap V2 вже задеплоєний.

### Крок 1. Компіляція контрактів

1. https://remix.ethereum.org → створити `AssetToken.sol`, `DefiIntegrator.sol`
   та `interfaces/IUniswapV2Router02.sol`, вставити код з теки `contracts/`.
2. Solidity Compiler → `0.8.24+` → Compile.
3. З Compilation Details скопіювати `ABI` та `BYTECODE` у відповідні файли
   `contracts/artifacts/AssetToken.json` і `DefiIntegrator.json`.

### Крок 2. Конфігурація

Скопіювати `appsettings.example.json` → `appsettings.json`, заповнити:

- `Web3Settings:RpcUrl`, `ChainId`, `PrivateKey` — тестовий гаманець з ETH
  у Sepolia (або дані локальної ноди);
- `Lab4Settings:RouterAddress` — адреса Router-контракту обраного DEX.

### Крок 3. Запуск

```bash
dotnet restore
dotnet run
```

Перший запуск: емітує два токени, розгортає `DefiIntegrator`, надає йому
approve, викликає `provideLiquidity` (створення пулу у **зовнішньому**
протоколі) і `swapTokens` (обмін через щойно створений пул). Адреси
зберігаються в `deployment-state.json` — повторний запуск не платить газ
за деплой повторно.