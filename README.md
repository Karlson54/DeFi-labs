# Лабораторна робота №7 — Сховище (Vault) з автоматичним реінвестуванням винагород

**Тема.** Розробка смарт-контракту сховища (Vault) для автоматизованого управління активами
та реінвестування винагород (Yield Aggregator, Auto-compounding).
**Виконав:** Рубан Андрій, група ПДМ-61.

Клієнт — консольний застосунок C# (.NET 8) + Nethereum, повністю самодостатній:
сам випускає два ERC-20 токени, наповнює пул у зовнішньому DEX (Uniswap V2 Router з лаб. №4),
розгортає `AutoCompoundVault`, відтворює життєвий цикл інвестора, а режим `--bot`
запускає off-chain кіпера, який автономно викликає `compound()`.

---

## 1. Ідея протоколу

Фарм-протоколи виплачують винагороду сторонніми токенами. Щоб заробити складний відсоток,
її треба забрати, продати за базовий актив і знову покласти на депозит — вручну це збиткове
через газ. Сховище робить це для всіх користувачів одразу:

1. Користувач вносить Токен A і отримує **акції** (`vRUBC`) — свою частку в загальному пулі.
2. На сховище надходить винагорода (Токен B).
3. Кіпер викликає `compound()`: уся винагорода одним обміном конвертується в Токен A
   через Router і залишається на балансі сховища.
4. Кількість акцій не змінилась, а `totalAssets` зросла — кожна акція подорожчала.

### Математика акцій

```
Перший депозит:  shares = assets − MINIMUM_SHARES   (MINIMUM_SHARES акцій спалюються)
Далі:            shares = assets × totalSupply / totalAssets
Зняття:          assets = shares × totalAssets / totalSupply
Ціна акції:      price  = totalAssets / totalSupply
```

### Архітектура

```
Інвестор (EOA)
   │ approve → deposit(assets) / withdraw(shares)
   ▼
AutoCompoundVault (ERC-20 vTokenA)  ◄── будь-який кіпер: compound()
   │ totalAssets = tokenA.balanceOf(vault)
   │ винагорода: tokenB.balanceOf(vault)  ◄── прямий переказ / фарм-протокол
   │ swapExactTokensForTokens([B → A], to = vault)
   ▼
Uniswap V2 Router  ──►  пул A/B (створюється клієнтом через addLiquidity)

Off-chain:
dotnet run -- --bot → KeeperBot (цикл 12 с)
                      └ rewardToken.balanceOf(vault) ≥ порогу? → vault.compound()
```

### Інваріанти безпеки

- **Checks-Effects-Interactions:** у `deposit` спершу `_mint`, потім `transferFrom`;
  у `withdraw` спершу `_burn`, потім `transfer`. Додатково `ReentrancyGuard`.
- **immutable:** `asset`, `rewardToken`, `router` фіксуються в конструкторі й не підмінюються.
- **Захист першого депозиту:** `MINIMUM_SHARES = 1000` акцій назавжди спалюються на `0xdead`.
- **Округлення на користь сховища:** усі ділення округлюють вниз.
- **Отримувач обміну — `address(this)`:** навіть зловмисний виклик `compound()` не виводить кошти назовні.
- **Спрощення (навчальні):** `amountOutMin = 1` у `compound()`. У продакшені мінімум рахують
  від оракула/TWAP, інакше можливі sandwich-атаки (MEV).

---

## 2. Структура проєкту

```
DeFi-labs/
├── contracts/
│   ├── AssetToken.sol                  # ERC-20 (з лаб. №4, без змін)
│   ├── AutoCompoundVault.sol           # сховище з автокомпаундингом
│   ├── interfaces/
│   │   └── IUniswapV2Router02.sol      # інтерфейс Router-а (з лаб. №4)
│   └── artifacts/
│       ├── AssetToken.json             # abi + bytecode (з лаб. №4)
│       └── AutoCompoundVault.json      # ← вставити BYTECODE з Remix
├── Models/
│   ├── Web3Settings.cs
│   ├── Lab7Settings.cs                 # параметри сценарію, пулу, бота
│   ├── DeploymentState.cs
│   ├── ContractArtifact.cs
│   ├── VaultResults.cs                 # результати кроків і звіти
│   └── Contracts/
│       ├── AssetTokenDefinition.cs
│       ├── UniswapRouterDefinition.cs
│       └── VaultDefinition.cs
├── Services/
│   ├── Web3Factory.cs
│   ├── ContractArtifactProvider.cs
│   ├── DeploymentStateStore.cs
│   ├── TokenService.cs                 # деплой, approve, balanceOf, transfer
│   ├── RouterService.cs                # addLiquidity у зовнішньому DEX
│   ├── VaultService.cs                 # deposit / withdraw / compound / знімок стану
│   ├── ScenarioRunner.cs               # оркестрація життєвого циклу інвестора
│   ├── KeeperBot.cs                    # автономний кіпер
│   └── ConsoleReportRenderer.cs
├── Program.cs                          # DI, режими запуску, обробка помилок
├── appsettings.example.json
├── DeFi.csproj
└── deployment-state-lab7.json          # генерується автоматично
```

---

## 3. Запуск

### Крок 0. Мережа з Uniswap V2

Як і в лаб. №4: публічний форк Uniswap V2 у Sepolia (адреса Router-а — з документації форку
або від викладача) або локальний Hardhat-форк, де Uniswap V2 уже розгорнуто.

### Крок 1. Компіляція контракту

1. https://remix.ethereum.org → створити `AutoCompoundVault.sol` та `interfaces/IUniswapV2Router02.sol`.
2. Solidity Compiler → `0.8.24+` → Compile (потрібен OpenZeppelin v5).
3. З Compilation Details скопіювати `BYTECODE` (поле `object`) у ключ `bytecode`
   файлу `contracts/artifacts/AutoCompoundVault.json`.
4. `contracts/artifacts/AssetToken.json` скопіювати з лаб. №4 без змін.

### Крок 2. Конфігурація

Скопіювати `appsettings.example.json` → `appsettings.json` (файл у `.gitignore`), заповнити:

- `Web3Settings:RpcUrl`, `ChainId`, `PrivateKey` — **тестовий** гаманець з ETH;
- `Lab7Settings:RouterAddress` — адреса Router-а обраного DEX;
- за потреби `DepositAmount` (1000), `RewardAmount` (100), `RewardThreshold`.

### Крок 3. Життєвий цикл інвестора (контрольне завдання)

```bash
dotnet restore
dotnet run
```

Сценарій:

1. розгортає токени A/B (якщо їх ще немає) та наповнює пул A/B у зовнішньому DEX;
2. розгортає `AutoCompoundVault`;
3. вносить 1000 Токена A, виводить кількість отриманих акцій;
4. надсилає 100 Токена B прямим ERC-20 переказом на сховище (імітація фарму);
5. викликає `compound()`: Токен B продається за Токен A через Router;
6. виводить `convertToAssets(баланс акцій)` до та після, знімає акції й доводить,
   що знято більше, ніж внесено.

Адреси зберігаються у `deployment-state-lab7.json`; повторний запуск не витрачає газ
на повторний деплой.

### Крок 4 (необов'язково). Бот-кіпер

```bash
dotnet run -- --stand      # підготовка стенду: деплой, пул, депозит (без compound)
dotnet run -- --bot        # термінал №1: бот опитує balanceOf(vault) кожні 12 с
dotnet run -- --donate     # термінал №2: імітація фарму — переказ винагороди
```

Приклад логу бота:

```
[12:04:19] Сховище 0x... | винагорода: 0 MFIAT
[12:04:31] Сховище 0x... | винагорода: 100 MFIAT
[12:04:31] [УВАГА] Винагорода перевищила поріг (10 MFIAT). Виклик compound()...
[12:04:55] [УСПІХ] Реінвестування виконано. Tx: 0x...
           Продано винагороди.: 100 MFIAT
           Отримано активу....: 99.5005
           totalAssets........: 1000 -> 1099.5005
           Ціна акції.........: 1 -> 1.0995
```

### Приклад виводу (скорочено, значення орієнтовні)

```
КРОК 3. ДЕПОЗИТ У СХОВИЩЕ (deposit)
  Внесено...............: 1000 RUBC
  Отримано акцій........: 1000
КРОК 4. ІМІТАЦІЯ ВИНАГОРОДИ
  Надіслано.............: 100 MFIAT
  Ціна 1 акції..........: 1 RUBC        ← не змінилась
КРОК 5. РЕІНВЕСТУВАННЯ (compound)
  Отримано активу.......: ≈99.5 RUBC
  totalAssets...........: 1000 -> ≈1099.5 RUBC
КРОК 6. ПЕРЕВІРКА ВАРТОСТІ АКЦІЙ
  Вартість акцій ДО compound.: 1000 RUBC
  Вартість акцій ПІСЛЯ.......: ≈1099.5 RUBC
  [OK] Внесено 1000, знято ≈1099.5 RUBC.
```

Пояснення до цифр: при першому депозиті 1000 wei акцій спалюються (`MINIMUM_SHARES`),
тому інвестор отримує на 10⁻¹⁵ акції менше (у консолі це округлюється до 8 знаків).
Прибуток залежить від глибини пулу: винагорода 100 B у пулі 50 000 / 50 000 з комісією 0.3%
обмінюється приблизно на 99.5 A.

---

## 4. Виконання контрольного завдання

| Вимога | Реалізація |
|---|---|
| Розгорнути `AutoCompoundVault` з адресами двох токенів і Router-а | `VaultService.DeployAsync` → конструктор `(asset_, rewardToken_, router_)` |
| Імітація винагороди прямим переказом Токена B | `ScenarioRunner.TransferRewardAsync` (`ERC-20 transfer` на адресу сховища) |
| Депозит 1000 одиниць, вивід отриманих акцій | `DepositStepAsync` (акції = приріст балансу `balanceOf`) |
| Виклик `compound()`, що продає Токен B за Токен A | `VaultService.CompoundAsync` |
| Перевірка `convertToAssets(баланс акцій)` | `VaultService.GetSnapshotAsync`, звіт кроку 6 |
| Доказ: знімаємо більше, ніж поклали | `withdraw` + перевірка `Withdraw.Assets > Deposit.Assets` |
| Off-chain автоматизація (Keeper) | `KeeperBot`, режим `--bot` |

**У звіті:** лістинг `AutoCompoundVault.sol`, код клієнта (`ScenarioRunner.cs`, `VaultService.cs`,
`KeeperBot.cs`) та скриншоти консолі (кроки 3, 4, 5, 6 — зміна вартості акцій).

---

## 5. Технологічний стек

| Шар | Технологія |
|---|---|
| Смарт-контракти | Solidity 0.8.24, OpenZeppelin v5 (ERC20, SafeERC20, ReentrancyGuard) |
| Зовнішній протокол | Uniswap V2 Router02 (композитність, лаб. №4) |
| Мережа | Sepolia або Hardhat-форк із розгорнутим Uniswap V2 |
| Клієнт та кіпер | C# (.NET 8), Nethereum 4.29, Microsoft.Extensions (DI, Options, Configuration) |
| Стан деплою | `deployment-state-lab7.json` |