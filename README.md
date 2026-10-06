# Лабораторна робота №5 — Кредитний протокол з емісією алгоритмічного стейблкоїна

**Тема.** Проєктування та програмна реалізація смарт-контракту кредитного протоколу
з функцією емісії алгоритмічного стейблкоїна (DeFi Lending, Over-collateralization).
**Виконав:** Рубан Андрій, група ПДМ-61.

Клієнт — консольний застосунок C# (.NET 8) + Nethereum, повністю самодостатній:
сам розгортає стейблкоїн `RubanUSD` та кредитне ядро `StableEngine`, передає ядру
права власності на токен і відтворює сценарій контрольного завдання.

---

## 1. Ідея протоколу

Користувач блокує ETH як заставу й отримує під неї стейблкоїн, але не більше, ніж
дозволяє коефіцієнт забезпечення CR = 150%. Якщо ціна ETH падає й позиція перестає
бути забезпеченою, вона стає неплатоспроможною.

Ключова метрика — Health Factor:

```
HF = (Collateral × 100 / CR) / Debt
```

- `HF ≥ 1` — позиція безпечна;
- `HF < 1` — позиція підлягає ліквідації;
- `Debt = 0` — `HF = +∞` (у контракті `type(uint256).max`).

### Архітектура

```
Користувач (EOA)
   │ depositCollateral{value}() / mintStablecoin() / burnStablecoin() / withdrawCollateral()
   ▼
StableEngine  ── зберігає заставу й борг, рахує Health Factor, тримає мок-оракул ціни
   │ mint(to, amount) / burn(from, amount)   ← лише onlyOwner
   ▼
StableCoin (ERC-20 + Ownable)  ── власник = StableEngine (після transferOwnership)
```

### Інваріанти безпеки

- `mintStablecoin`: спочатку збільшується борг, потім `_revertIfHealthFactorIsBroken`
  (revert скасовує все), і лише після цього зовнішній `mint`.
- `withdrawCollateral`: спочатку зменшується застава, потім та сама перевірка HF.
- `burnStablecoin`: перевірка HF не потрібна, бо погашення лише покращує здоров'я позиції.
- Патерн Checks-Effects-Interactions + `ReentrancyGuard` (зняття ETH іде через `call`).

---

## 2. Структура проєкту

```
DeFi-labs/
├── contracts/
│   ├── StableCoin.sol              # ERC-20 + Ownable: mint/burn лише для власника
│   ├── StableEngine.sol            # кредитне ядро (застава, борг, Health Factor)
│   └── artifacts/
│       ├── StableCoin.json         # abi + bytecode для деплою через Nethereum
│       └── StableEngine.json
├── Models/
│   ├── Web3Settings.cs             # конфіг RPC-вузла та приватного ключа
│   ├── Lab5Settings.cs             # параметри стейблкоїна та сценарію
│   ├── DeploymentState.cs          # адреси розгорнутих контрактів
│   ├── ContractArtifact.cs
│   ├── ScenarioResults.cs          # результати кроків сценарію та звіт
│   └── Contracts/
│       ├── StableCoinDefinition.cs # типізовані повідомлення Nethereum (токен)
│       └── StableEngineDefinition.cs
├── Services/
│   ├── Web3Factory.cs
│   ├── ContractArtifactProvider.cs
│   ├── DeploymentStateStore.cs     # читання/запис deployment-state.json
│   ├── StablecoinService.cs        # деплой токена, transferOwnership, approve
│   ├── StableEngineService.cs      # деплой ядра, deposit/mint/burn/withdraw, HF
│   ├── ScenarioRunner.cs           # оркестрація сценарію контрольного завдання
│   └── ConsoleReportRenderer.cs    # консольний звіт
├── Program.cs                      # DI та обробка помилок
├── appsettings.example.json
├── DeFi.csproj
└── deployment-state.json           # генерується автоматично
```

---

## 3. Запуск

### Крок 0. Локальна нода

```bash
cd ~/hardhat-node
npx hardhat node
```

Залиште вікно відкритим. Перший акаунт Hardhat має 10000 тестових ETH — їх
вистачає на заставу й газ.

### Крок 1. Компіляція контрактів

1. https://remix.ethereum.org → створити `StableCoin.sol` та `StableEngine.sol`
   в одній теці, вставити код з `contracts/`.
2. Solidity Compiler → `0.8.24+` → Compile (потрібен OpenZeppelin v5).
3. З Compilation Details скопіювати `ABI` та `BYTECODE` (поле `object`) у файли
   `contracts/artifacts/StableCoin.json` і `StableEngine.json`.

### Крок 2. Конфігурація

Скопіювати `appsettings.example.json` → `appsettings.json`, заповнити:

- `Web3Settings:PrivateKey` — тестовий ключ першого акаунта Hardhat;
- `Lab5Settings:Stablecoin` — назва стейблкоїна (за завданням — прізвище + USD);
- `Lab5Settings:InitialEthUsdPrice` / `CollateralEth` / `WithdrawEth` — параметри
  сценарію (за замовчуванням $2000, 2 ETH, 1 ETH).

### Крок 3. Запуск

```bash
dotnet restore
dotnet run
```

Сценарій:

1. розгортає `RubanUSD` та `StableEngine`, передає ядру власність на токен;
2. вносить 2 ETH застави (при ціні $2000);
3. програмно підбирає й випускає максимальну суму стейблкоїнів;
4. намагається зняти 1 ETH → транзакція відкочується, скрипт перехоплює помилку
   й виводить, що захист спрацював;
5. додатково: погашає борг (`approve` + `burnStablecoin`) і знімає 1 ETH успішно.

Адреси зберігаються в `deployment-state.json`. Якщо адреса вже не містить коду
(наприклад, нода Hardhat була перезапущена), клієнт сам розгорне контракти заново.

### Приклад виводу (скорочено)

```
КРОК 3. ЕМІСІЯ МАКСИМАЛЬНОЇ СУМИ СТЕЙБЛКОЇНІВ (mintStablecoin)
  Випущено..............: 2666.66666667 RUBUSD
  Позиція:
    Застава.............: 2 ETH ($4000)
    Борг................: 2666.66666667 RUBUSD
    Health Factor.......: 1

КРОК 4. СПРОБА ЗНЯТИ ЗАСТАВУ (withdrawCollateral) — ОЧІКУЄТЬСЯ REVERT
  Спроба зняти..........: 1 ETH
  Причина відкату.......: StableEngine: health factor broken
  [OK] Захист спрацював успішно: транзакцію відхилено, бо Health Factor впав би нижче 1.
```

Максимальний борг при заставі $4000: `4000 × 100 / 150 = 2666.(6)`. Округлення в
контракті йде вниз, тому Health Factor дорівнює рівно 1.0 і транзакція проходить.
Після зняття 1 ETH застава була б $2000, ліміт боргу — $1333, тобто `HF ≈ 0.5`.

---

## 4. Виконання контрольного завдання

| Вимога | Реалізація |
|---|---|
| Розгорнути StableCoin і StableEngine, назвати за прізвищем | `Lab5Settings:Stablecoin` (`RubanUSD`), `ScenarioRunner` |
| `burnStablecoin(uint256 amount)` | `StableEngine.burnStablecoin` — `transferFrom` + `burn`, борг зменшується |
| `withdrawCollateral(uint256 amount)` з перевіркою HF | `StableEngine.withdrawCollateral` → `_revertIfHealthFactorIsBroken` |
| Депозит 2 ETH при $2000 | `StableEngineService.DepositCollateralAsync` |
| Програмний підбір максимальної емісії | `StableEngineService.MintMaxAsync` (формула з даних ланцюга) |
| Перехоплення revert при знятті 1 ETH | `StableEngineService.TryWithdrawCollateralAsync` |


---

## 5. Технологічний стек

| Шар | Технологія |
|---|---|
| Смарт-контракти | Solidity 0.8.24, OpenZeppelin v5 (ERC20, Ownable, ReentrancyGuard) |
| Локальна мережа | Hardhat Network |
| Клієнт | C# (.NET 8), Nethereum 4.29, Microsoft.Extensions (DI, Options, Configuration) |
| Стан деплою | `deployment-state.json` |