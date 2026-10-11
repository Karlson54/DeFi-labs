# Лабораторна робота №9 — Вектори атак на DeFi (Flash Loan) та децентралізоване управління (DAO)

**Тема.** Моделювання векторів атак на DeFi-протоколи (Flash Loan) та розробка
смарт-контракту для децентралізованого управління (DAO).
**Виконав:** Рубан Андрій, група ПДМ-61.

Клієнт — консольний застосунок C# (.NET 8) + Nethereum, повністю самодостатній. Він має два режими:

- `dotnet run` — **контрольне завдання**: розгортає `SimpleDAO` та `MockProtocol`, передає DAO
  права власності, кодує `setFee(10)`, створює пропозицію, голосує, чекає кінця голосування
  і виконує пропозицію;
- `dotnet run -- --attack` — **симуляція Flash Loan-атаки** на кредитор зі спот-оракулом і
  та сама атака на кредитор із захищеним оракулом.

---

## 1. Ідея

### Flash Loan-атака (п. 9.2)

Миттєва позика не потребує застави, але має бути повернена з комісією (0.09%) у тій самій
транзакції. Якщо ні, EVM відкочує все. Якщо кредитор довіряє спот-ціні AMM-пулу, цю ціну
можна спотворити за позичені гроші.

```
Чистий стенд: пул 1000 A / 2000 B  (ціна 1 A = 2 B)
Кредитор має 50 000 B; ціну A бере з пулу (вразливий)

FlashLoanAttacker.attack(10 000 B)
 └─ FlashLoanProvider.flashLoan ── переказ 10 000 B ──► executeOperation (коллбек)
      1. swapBForA(10 000 B) у пулі   → ціна 1 A стрибає з 2 до ≈69 B
      2. depositCollateral(≈826 A)    → заставу оцінено в ≈57 000 B
      3. borrow(maxBorrowable ≈38 000 B)
      4. повернути 10 000 B + 9 B комісії, решта (≈28 000 B) — прибуток
```

Це орієнтовні цифри: точний результат залежить від динамічної комісії `DexPool` (5% для "кита").
Той самий протокол, підключений до `TrustedPriceOracle` (імітація Chainlink з лаб. №6),
має іншу картину. Чесна вартість куплених A ≈ 1650 B, ліміт позики ≈ 1100 B, що менше за
10 009 B боргу. Контракт атакуючого не може повернути позику, `require` відкочує все.

### DAO (п. 9.3)

```
Власник токенів (EOA)
   │ createProposal(target, payload, description)
   │ vote(id, support)                       ← вага = баланс governance-токена
   │ executeProposal(id)  (після VOTING_PERIOD)
   ▼
SimpleDAO ── target.call(payload) ──►  MockProtocol.setFee(10)   (onlyOwner)
                                        owner == SimpleDAO (після transferOwnership)
```

`payload` — це 4 байти селектора `keccak256("setFee(uint256)")` плюс ABI-кодований аргумент.
Клієнт будує його через ABI з артефакту (`Function.GetData`).

### Архітектура проєкту

```
 Режим DAO:   ScenarioRunner → ProtocolService / DaoService / TokenService
 Режим атаки: AttackRunner   → FlashLoanService / TokenService
 Спільне:     ContractDeployer (узагальнений деплой), DeploymentSession (каскад перевикористання),
              Web3Factory, ContractArtifactProvider, DeploymentStateStore, ConsoleReportRenderer
```

### Інваріанти безпеки

- **Callback-перевірки:** `executeOperation` приймає виклики лише від провайдера, лише від
  власної ініціації та лише для очікуваного токена.
- **Перевірка повернення позики за балансом:** `balanceAfter >= balanceBefore + fee`, інакше Revert.
  Комісія округлюється вгору (на користь кредитора).
- **Checks-Effects-Interactions + `ReentrancyGuard`** у кредиторі та DAO. У `executeProposal`
  `executed = true` виставляється до зовнішнього `call`.
- **Dependency Injection оракула** (патерн лаб. №4): `OracleLender` не знає, звідки ціна. Захист
  зводиться до вибору джерела.
- **Захищений оракул** відкидає нульову й застарілу ціну (`MAX_PRICE_AGE`, як у лаб. №6).
- **`immutable`:** адреси токенів, оракула, провайдера й `governanceToken` не підмінюються.
- **Кворум і більшість** у DAO: `votesFor > votesAgainst` та участь ≥ 10% від `totalSupply`.
- **Подвійне голосування** заборонене `hasVoted` для однієї адреси.
- **Навчальні спрощення:**
    - `FlashLoanAttacker` — це демонстраційний експлойт для власного стенда, не інструмент для чужих протоколів;
    - вага голосу береться з поточного балансу без snapshot (див. відповідь 4);
    - у `OracleLender` немає виведення застави й ліквідацій.

---

## 2. Структура проєкту

```
DeFi-labs/
├── contracts/
│   ├── AssetToken.sol                  # ERC-20 (з лаб. №4/№7, без змін)
│   ├── MockProtocol.sol                # мішень: fee + setFee (onlyOwner)
│   ├── SimpleDAO.sol                   # пропозиції, голосування, executeProposal
│   ├── SpotPriceOracle.sol             # ВРАЗЛИВИЙ оракул (резерви пулу)
│   ├── TrustedPriceOracle.sol          # ЗАХИЩЕНИЙ оракул (імітація Chainlink)
│   ├── OracleLender.sol                # кредитор: застава A, позика B
│   ├── FlashLoanProvider.sol           # кредитор миттєвих позик (0.09%)
│   ├── FlashLoanAttacker.sol           # симуляція атаки
│   ├── interfaces/
│   │   ├── IPriceOracle.sol  IDexPool.sol  IFlashLoan.sol  IOracleLender.sol
│   └── artifacts/
│       ├── AssetToken.json, DexPool.json    # з лаб. №7 / №3, без змін
│       └── MockProtocol.json  SimpleDAO.json  SpotPriceOracle.json  TrustedPriceOracle.json
│           OracleLender.json  FlashLoanProvider.json  FlashLoanAttacker.json   # ← вставити abi + bytecode з Remix
├── Models/
│   ├── Web3Settings.cs  ContractArtifact.cs        # без змін
│   ├── Lab9Settings.cs  DeploymentState.cs
│   ├── GovernanceResults.cs  AttackResults.cs
│   └── Contracts/
│       ├── AssetTokenDefinition.cs                 # з лаб. №7
│       ├── OwnableDefinition.cs  GovernanceDefinition.cs  FlashLoanDefinition.cs
├── Services/
│   ├── Web3Factory.cs  ContractArtifactProvider.cs  DeploymentStateStore.cs  TokenService.cs  # без змін
│   ├── ContractDeployer.cs  DeploymentSession.cs  RevertReasonParser.cs
│   ├── ProtocolService.cs  DaoService.cs  FlashLoanService.cs
│   ├── ScenarioRunner.cs   # контрольне завдання (DAO)
│   ├── AttackRunner.cs     # симуляція атаки
│   └── ConsoleReportRenderer.cs
├── Program.cs
├── appsettings.example.json
├── DeFi.csproj
└── deployment-state-lab9.json              # генерується автоматично
```

---

## 3. Запуск

### Крок 0. Локальна нода
```bash
cd ~/hardhat-node
npx hardhat node
```
Залиште вікно відкритим. Перший акаунт Hardhat має 10000 тестових ETH.

### Крок 1. Компіляція контрактів
1. https://remix.ethereum.org → створити файли з теки `contracts/` (разом з `interfaces/`).
2. Solidity Compiler → `0.8.24+` → Compile (потрібен OpenZeppelin v5).
3. Для кожного з 7 нових контрактів скопіювати `ABI` **і** `BYTECODE` (поле `object`) у
   `contracts/artifacts/<Назва>.json`. ABI `MockProtocol` має бути повним: з нього
   кодується `setFee`.
4. `AssetToken.json` та `DexPool.json` скопіювати з лаб. №7 та №3 без змін.

### Крок 2. Конфігурація
Скопіювати `appsettings.example.json` → `appsettings.json` (файл у `.gitignore`).
Для Hardhat змінювати нічого не потрібно.

### Крок 3. Контрольне завдання (DAO)
```bash
dotnet restore
dotnet run
```
Сценарій:
1. розгортає governance-токен, `MockProtocol` (комісія 3) та `SimpleDAO`;
2. передає DAO права власності на протокол і показує, що прямий `setFee` відкочується;
3. кодує `setFee(10)` і створює пропозицію;
4. голосує "ЗА" з гаманця, що тримає токени;
5. чекає кінця голосування (`VOTING_PERIOD = 1 minutes` у контракті) і викликає `executeProposal`;
6. перевіряє, що `fee` дорівнює 10.

Для зручного скриншота на Blockchain Explorer використовуйте Sepolia (змініть `RpcUrl`/`ChainId`).
Повторний запуск перевикористовує контракти. Щоб побачити зміну, змініть
`Lab9Settings:Dao:NewFee` або видаліть `deployment-state-lab9.json`.
Після перезапуску ноди контракти розгорнуться автоматично.

### Крок 4. Flash Loan-атака
```bash
dotnet run -- --attack
```
Кожен запуск розгортає чистий стенд, бо атака змінює стан пулу й кредитора.
Рекомендована мережа — локальна нода, бо розгортається ~15 контрактів.

### Приклад виводу (скорочено, значення орієнтовні)
```
КРОК 2. АТАКА НА ВРАЗЛИВОГО КРЕДИТОРА (ціна береться зі спот-пулу)
  Миттєва позика........: 10000 MFIAT, комісія 9 MFIAT
  Спот-ціна ДО..........: 2 MFIAT за 1 RUBC
  Спот-ціна ПІСЛЯ.......: 69 MFIAT за 1 RUBC
  Позичено у кредитора..: 38000 MFIAT під заставу 826.08 RUBC
  Чистий прибуток.......: ≈27991 MFIAT

КРОК 3. ТА САМА АТАКА НА КРЕДИТОРА З ОРАКУЛОМ-ФІДОМ
  Результат.............: ВІДКАТ
  Причина відкату.......: FlashLoanAttacker: attack unprofitable
  [OK] Захист спрацював: ...

КРОК 6. ПЕРЕВІРКА РЕЗУЛЬТАТУ (режим DAO)
  Комісія ДО............: 3
  Комісія ПІСЛЯ.........: 10
  [OK] fee у MockProtocol змінилась на 10 після виклику від імені DAO.
```

---

## 4. Виконання контрольного завдання

| Вимога | Реалізація |
|---|---|
| Розгорнути `SimpleDAO` з governance-токеном | `DaoService.DeployAsync` → конструктор `(governanceToken_)` |
| `MockProtocol` з `fee` та `setFee` (onlyOwner) | `MockProtocol.sol` (`Ownable`) |
| `transferOwnership` до DAO | `ProtocolService.TransferOwnershipAsync` + доказ відкату прямого `setFee` |
| Кодування `setFee(10)` і `createProposal` | `ProtocolService.EncodeSetFeeAsync`, `DaoService.CreateProposalAsync` |
| `vote` з гаманця з токенами | `DaoService.VoteAsync` |
| Очікування терміну та `executeProposal` | `ScenarioRunner.WaitForDeadlineAsync`, `DaoService.ExecuteProposalAsync` |
| Доказ `fee == 10` | перевірка `GetFeeAsync` + крок 6 звіту |
| Симуляція Flash Loan-атаки (п. 9.2) | `FlashLoanAttacker`, режим `--attack` |
| Аналіз реальної атаки | розділ 7 |

**У звіті:** лістинги контрактів і скриптів, скриншот Blockchain Explorer (або консолі), де
`fee = 10` після виклику від DAO, і розділ 7.

---

## 5. Обмеження навчальної реалізації

- `SimpleDAO` не має snapshot, timelock, делегування та скасування пропозицій.
- `OracleLender` не має ліквідацій і виведення застави; ліквідність приходить прямим переказом.
- `TrustedPriceOracle` — імітація: ціну виставляє власник. На реальній мережі тут стоїть
  `AggregatorV3Interface` (див. лаб. №6).
- `FlashLoanAttacker` призначений лише для власного тестового стенда.

---

## 6. Аналіз реальної атаки: Euler Finance (13 березня 2023)

Euler Finance — кредитний протокол на Ethereum. Було викрадено ≈197 млн $ у кількох активах
(DAI, USDC, wBTC, stETH). Вектор — не маніпуляція ціною, а помилка в логіці перевірки
платоспроможності, яку Flash Loan зробив можливою на великій суммі. Атакуючий взяв миттєву позику
≈30 млн DAI в Aave, поклав частину в Euler і за допомогою функції `mint` набрав
плечову позицію (борг у dTokens і застава в eTokens). Далі він викликав `donateToReserves`:
функція дозволяла "пожертвувати" власні eTokens у резерви, але **не перевіряла стан здоров'я
позиції після цього**. Позиція одразу ставала неплатоспроможною, і другий акаунт атакуючого
ліквідував її зі знижкою, забравши більше застави, ніж погашав. Вкрадене виводилось, позика
повертася з комісією — все в одній транзакції. Урок збігається з темою лаби: у протоколі
з атомарними складними викликами **кожна** функція, що змінює позицію, має закінчуватись
перевіркою інваріанта (як `_revertIfHealthFactorIsBroken` у лаб. №5), а аудит має покривати
взаємодію функцій, а не лише кожну окремо. Згодом більшу частину коштів атакуючий повернув
після переговорів.

---

## 7. Технологічний стек

| Шар | Технологія |
|---|---|
| Смарт-контракти | Solidity 0.8.24, OpenZeppelin v5 (Ownable, ERC20, SafeERC20, ReentrancyGuard) |
| Зовнішні компоненти | `DexPool` (лаб. №3), патерн оракула з лаб. №6 |
| Мережа | Hardhat (локально) або Sepolia |
| Клієнт | C# (.NET 8), Nethereum 4.29, Microsoft.Extensions (DI, Options, Configuration) |
| Стан деплою | `deployment-state-lab9.json` |