# Лабораторна робота №6 — Децентралізовані оракули та Off-chain агент

**Тема.** Інтеграція децентралізованих оракулів (Chainlink) та розробка автономного
off-chain агента (Keeper / Liquidator) для підтримки платоспроможності кредитного протоколу.
**Виконав:** Рубан Андрій, група ПДМ-61.

Клієнт — консольний застосунок C# (.NET 8) + Nethereum. Він сам розгортає стейблкоїн
`RubanUSD` та кредитне ядро `StableEngine` (з реальним оракулом Chainlink ETH/USD),
створює боргову позицію з Health Factor ≈ 1.1, а окремий режим `--bot` запускає
бота-ліквідатора, який автономно ліквідує неплатоспроможну позицію.

---

## 1. Ідея

У лабораторній №5 ціна ETH була мок-змінною, яку адміністратор міняв вручну. Тепер:

1. **Ціну дає Chainlink Data Feed.** `StableEngine.getEthUsdPrice()` читає
   `latestRoundData()`, відкидає від'ємну чи застарілу ціну й масштабує її з 8 знаків
   до 18 (множення на `1e10`).
2. **Додано примусову ліквідацію.** Якщо `HF < 1`, будь-хто може викликати
   `liquidate(user)`: погасити борг власними стейблкоїнами й отримати заставу
   з премією 10% (`LIQUIDATION_BONUS`).
3. **Додано бота.** Контракти не виконуються самі, тому за `HF` стежить зовнішній агент.

```
HF = (Collateral × 100 / CR) / Debt
HF ≥ 1  — позиція безпечна;   HF < 1 — позиція підлягає ліквідації
```

### Архітектура

```
 Chainlink ETH/USD Data Feed  (Sepolia: 0x694A...5306)
          │ latestRoundData()  — ціна, 8 знаків
          ▼
 StableEngine ── getEthUsdPrice() ×1e10 → 18 знаків; перевірка свіжості
   │  depositCollateral / mintStablecoin / burnStablecoin / withdrawCollateral
   │  liquidate(user)            ◄── транзакція бота, якщо HF < 1
   │  simulateInsolvency(...)    ◄── ЛАБОРАТОРНИЙ бекдор (onlyOwner)
   │ mint / burn (onlyOwner)
   ▼
 StableCoin (ERC-20 + Ownable, власник = StableEngine)

 Off-chain:
 dotnet run -- --bot  →  LiquidatorBot (цикл опитування, 12 с)
                          └ LiquidatorService (гаманець ліквідатора)
                              getHealthFactor (view, без газу) → HF < 1? → approve → liquidate
```

### Механіка ліквідації

```
debt              = stablecoinMinted[user]
baseCollateral    = debt / ціна_ETH                 (ETH, що точно покриває борг)
collateralToSeize = baseCollateral × (100 + 10) / 100
```

Контракт обнуляє борг, зменшує заставу позичальника, забирає й спалює стейблкоїни
ліквідатора, а потім переказує йому `collateralToSeize` ETH. Порядок дій —
Checks-Effects-Interactions, плюс `ReentrancyGuard`.

### Інваріанти безпеки

- Ліквідація можлива лише при `HF < 1`; самоліквідація заборонена.
- Застаріла (`MAX_PRICE_AGE`) або нульова ціна оракула дає revert, а не розрахунок за хибним курсом.
- `simulateInsolvency` доступна лише власнику й існує тільки для лабораторної.
  **У реальному протоколі така функція — бекдор, її необхідно видаляти.**

---

## 2. Структура проєкту

```
DeFi-labs/
├── contracts/
│   ├── StableCoin.sol              # ERC-20 + Ownable (без змін відносно №5)
│   ├── StableEngine.sol            # + Chainlink, liquidate, simulateInsolvency
│   └── artifacts/
│       ├── StableCoin.json
│       └── StableEngine.json       # ПЕРЕКОМПІЛЮВАТИ (нова версія контракту)
├── Models/
│   ├── Web3Settings.cs
│   ├── Lab6Settings.cs             # параметри сценарію, оракула, бота
│   ├── DeploymentState.cs
│   ├── ContractArtifact.cs
│   ├── ScenarioResults.cs          # результати кроків, звіти, результат ліквідації
│   └── Contracts/
│       ├── StableCoinDefinition.cs
│       └── StableEngineDefinition.cs
├── Services/
│   ├── Web3Factory.cs              # основний гаманець + окремий клієнт ліквідатора
│   ├── ContractArtifactProvider.cs
│   ├── DeploymentStateStore.cs
│   ├── WalletService.cs            # баланс і переказ ETH
│   ├── StablecoinService.cs
│   ├── StableEngineService.cs      # деплой, депозит, емісія до цільового HF, бекдор
│   ├── LiquidatorService.cs        # дії гаманця ліквідатора (HF, approve, liquidate)
│   ├── LiquidatorBot.cs            # автономний цикл моніторингу
│   ├── ScenarioRunner.cs           # підготовка стенду та виклик бекдора
│   └── ConsoleReportRenderer.cs
├── Program.cs                      # DI, режими запуску, обробка помилок
├── appsettings.example.json
├── DeFi.csproj
└── deployment-state-lab6.json      # генерується автоматично
```

> Файл `Models/Lab6Settings.cs` слід видалити: клас `StablecoinSettings` тепер у `Lab6Settings.cs`.

---

## 3. Запуск (Sepolia)

### Крок 0. Підготовка

- Два **тестових** гаманці: позичальник (він же власник протоколу) і ліквідатор.
  Ключі від гаманців з реальними коштами використовувати не можна.
- Тестовий ETH у Sepolia на позичальнику: приблизно 0.05 ETH
  (деплой + застава 0.01 + 0.02 ETH, які піднімуться ліквідатору на газ).
- RPC-endpoint Sepolia (Infura/Alchemy).

### Крок 1. Компіляція контрактів

1. https://remix.ethereum.org → створити `StableCoin.sol` і `StableEngine.sol` в одній теці.
2. Solidity Compiler → `0.8.24+` → Compile (OpenZeppelin v5 та `@chainlink/contracts`
   Remix підтягне з npm автоматично; локально в Hardhat/Foundry: `npm install @chainlink/contracts`).
3. З Compilation Details скопіювати `ABI` та `BYTECODE` (поле `object`) у
   `contracts/artifacts/StableEngine.json`. `StableCoin.json` лишається без змін.

### Крок 2. Конфігурація

Скопіювати `appsettings.example.json` → `appsettings.json` (файл у `.gitignore`), заповнити:

- `Web3Settings:RpcUrl`, `ChainId` = `11155111`, `PrivateKey` — ключ позичальника;
- `Lab6Settings:LiquidatorPrivateKey` — ключ **іншого** гаманця (ліквідатор);
- `Lab6Settings:PriceFeedAddress` — `0x694AA1769357215DE4FAC081bf1f309aDC325306` (ETH/USD, Sepolia).

Необов'язково, для локальної перевірки: `npx hardhat node --fork <SEPOLIA_RPC>` і `ChainId` = `31337`.
Адреса Data Feed на форку та сама.

### Крок 3. Підготовка стенду

```bash
dotnet restore
dotnet run
```

Скрипт розгортає `RubanUSD` і `StableEngine` (з адресою оракула в конструкторі), передає
ядру власність на токен, вносить заставу й випускає стейблкоїни так, щоб `HF ≈ 1.1`.
Потім він докидає ліквідатору ETH на газ і стейблкоїни для викупу боргу.
Повторний запуск безпечний: наявні контракти та застава перевикористовуються.

### Крок 4. Бот-ліквідатор (термінал №1)

```bash
dotnet run -- --bot
```

Бот щоразу опитує `getHealthFactor` (безкоштовний view-запит) і пише в консоль:
`Позиція 0x... | HF: 1.1`.

### Крок 5. Штучний крах (термінал №2)

```bash
dotnet run -- --crash
```

Бекдор `simulateInsolvency` зменшує `collateralDeposited` позичальника на
`CrashCollateralPercent` (20%), тож `HF = 1.1 × 0.8 = 0.88`. У терміналі бота:

```
[12:04:31] Позиція 0x... | HF: 0.88
[12:04:31] [УВАГА] Виявлено неплатоспроможну позицію 0x...! Ініціалізація ліквідації...
[12:04:55] [УСПІХ] Позицію ліквідовано у блоці 6543210. Tx: 0x...
```

Це і є момент спрацювання бота для звіту.

> **Обмеження:** щоб ліквідація була прибутковою без втрат застави, треба
> `CrashCollateralPercent` ≲ 33% при `TargetHealthFactor = 1.1`
> (застава мусить покрити борг + 10% премії).

---

## 4. Виконання контрольного завдання

| Вимога | Реалізація |
|---|---|
| Розгорнути контракти в Sepolia з адресою оракула ETH/USD | `StableEngineService.DeployAsync` → конструктор `StableEngine(stablecoin_, priceFeed_)` |
| Депозит і емісія, HF ≈ 1.1 | `ScenarioRunner` → `MintToHealthFactorAsync` (борг = `maxDebt / 1.1`) |
| Бот-ліквідатор (C#/Nethereum) | `LiquidatorBot` + `LiquidatorService`, режим `--bot` |
| Бекдор для штучного краху | `StableEngine.simulateInsolvency` (onlyOwner), режим `--crash` |
| Фіксація спрацювання бота | лог терміналу + хеш транзакції `liquidate` у Sepolia Etherscan |

**У звіті:** лістинг `StableEngine.sol`, код бота (`LiquidatorBot.cs`, `LiquidatorService.cs`),
скриншот терміналу бота з `[УСПІХ]` та хеш транзакції ліквідації.

---

## 5. Технологічний стек

| Шар | Технологія |
|---|---|
| Смарт-контракти | Solidity 0.8.24, OpenZeppelin v5 (ERC20, Ownable, ReentrancyGuard), Chainlink `AggregatorV3Interface` |
| Мережа | Sepolia (опційно — Hardhat-форк Sepolia) |
| Клієнт та бот | C# (.NET 8), Nethereum 4.29, Microsoft.Extensions (DI, Options, Configuration) |
| Стан деплою | `deployment-state-lab6.json` |