# Лабораторна робота №8 — Мережі другого рівня (Layer 2) та крос-чейн взаємодія через Chainlink CCIP

**Тема.** Розгортання смарт-контрактів у мережах другого рівня (Layer 2) та реалізація
програмної взаємодії з використанням крос-чейн протоколів.
**Виконав:** Рубан Андрій, група ПДМ-61.

Клієнт — консольний застосунок C# (.NET 8) + Nethereum. Він сам розгортає контракт-отримувач
`CrossChainReceiver` в Arbitrum Sepolia (L2) та контракт-відправник `CrossChainMessenger`
в Ethereum Sepolia (L1), поповнює месенджер токенами LINK, розраховує комісію й ініціює
крос-чейн повідомлення. Режим `--track` запускає off-chain агента, який дочікується
доставки повідомлення в L2.

---

## 1. Ідея

L2-мережі (Arbitrum, Base, Optimism) EVM-сумісні: той самий Solidity, той самий байткод,
той самий JSON-RPC. Для C#-клієнта вони відрізняються від L1 лише `RpcUrl` та `ChainId`.
Тому один і той самий приватний ключ дає одну й ту саму адресу в обох мережах, а клієнт
тримає два `Web3`-екземпляри — по одному на мережу.

Але контракт в одній мережі не бачить стан іншої. Для обміну даними використовується
Chainlink CCIP: контракт-відправник передає повідомлення локальному Router-у, децентралізована
мережа оракулів переносить його, а Router у цільовій мережі викликає `ccipReceive` отримувача.

### Архітектура

```
 C# клієнт (Nethereum)
   │ deploy / transfer LINK / sendMessage                  │ deploy / читання lastMessageId
   ▼  (Web3Settings: Sepolia)                              ▼  (Lab8Settings:Destination: Arbitrum Sepolia)
 CrossChainMessenger (L1)                              CrossChainReceiver (L2)
   │ getFee() ─► Router.getFee                              ▲ ccipReceive() — лише від Router
   │ approve(LINK) ─► Router                                │
   │ ccipSend() ─────────────────────────────────┐          │
   ▼                                             ▼          │
 CCIP Router (Sepolia) ══► мережа оракулів Chainlink ══► CCIP Router (Arbitrum Sepolia)
   │ emit MessageSent(messageId, ...)
   ▼
 клієнт зберігає messageId у deployment-state-lab8.json

 Off-chain:
 dotnet run -- --track → DeliveryTracker (цикл 15 с)
                         └ receiver.lastMessageId == messageId? → Status: Success
```

### Як формується повідомлення

```
receiver     = abi.encode(адреса отримувача)     // універсальний байтовий масив (CCIP підтримує й не-EVM мережі)
data         = abi.encode(текст)
tokenAmounts = []                                // передаємо дані, а не активи
extraArgs    = EVMExtraArgsV1{ gasLimit }        // газ на виконання в цільовій мережі
feeToken     = LINK
```

### Інваріанти безпеки

- **onlyOwner на `sendMessage`:** комісію контракт платить зі свого LINK-балансу, тому без обмеження
  будь-хто міг би спустошити баланс.
- **immutable:** `router` та `linkToken` фіксуються в конструкторі й не підмінюються.
- **Перевірка балансу перед відправкою:** `balanceOf >= fees`, інакше зрозумілий revert.
- **Мінімальний allowance:** Router отримує approve рівно на суму комісії (`forceApprove`).
- **onlyRouter на отримувачі:** базовий `CCIPReceiver` дозволяє викликати `ccipReceive` лише Router-у,
  тож підробити повідомлення напряму неможливо.
- **Спрощення (навчальні):** отримувач приймає повідомлення від будь-якого відправника. У продакшені
  додають allowlist мереж-відправників (`sourceChainSelector`) та адрес-відправників.

---

## 2. Структура проєкту

```
DeFi-labs/
├── contracts/
│   ├── CrossChainMessenger.sol         # відправник повідомлень (L1)
│   ├── CrossChainReceiver.sol          # отримувач-пустушка (L2)
│   └── artifacts/
│       ├── CrossChainMessenger.json    # ← вставити BYTECODE з Remix
│       └── CrossChainReceiver.json     # ← вставити BYTECODE з Remix
├── Models/
│   ├── Web3Settings.cs                 # RPC та ключ вихідної мережі (з лаб. №7)
│   ├── Lab8Settings.cs                 # Router-и, selector, текст, трекер
│   ├── DeploymentState.cs
│   ├── ContractArtifact.cs
│   ├── CcipResults.cs                  # результати кроків і звіти
│   └── Contracts/
│       ├── CrossChainMessengerDefinition.cs   # + DTO події MessageSent
│       ├── CrossChainReceiverDefinition.cs
│       └── LinkTokenDefinition.cs
├── Services/
│   ├── Web3Factory.cs                  # два клієнти: L1 та L2, один ключ
│   ├── ContractArtifactProvider.cs
│   ├── DeploymentStateStore.cs
│   ├── LinkService.cs                  # balanceOf / transfer LINK
│   ├── MessengerService.cs             # деплой, getFee, sendMessage, розбір MessageSent
│   ├── ReceiverService.cs              # деплой у L2, читання lastMessage
│   ├── ScenarioRunner.cs               # оркестрація контрольного завдання
│   ├── DeliveryTracker.cs              # агент очікування доставки
│   └── ConsoleReportRenderer.cs
├── Program.cs                          # DI, режими запуску, обробка помилок
├── appsettings.example.json
├── DeFi.csproj
└── deployment-state-lab8.json          # генерується автоматично
```

---

## 3. Запуск

### Крок 0. Мережі, гаманець і кошти

1. У MetaMask додайте **Ethereum Sepolia** та **Arbitrum Sepolia** (Chain ID `11155111` і `421614`).
2. Використовуйте лише **тестовий** гаманець. Ключ від гаманця з реальними коштами в конфіг класти не можна.
3. Тестовий ETH для газу: у Sepolia — будь-який Sepolia faucet; в Arbitrum Sepolia — faucet або міст
   із Sepolia (газ потрібен для деплою отримувача).
4. Тестові LINK у Sepolia: https://faucets.chain.link/sepolia (орієнтовно 1–2 LINK достатньо).
5. Адреси Router-ів, LINK та **Chain Selector**-и перевіряйте в довіднику CCIP Directory:
   https://docs.chain.link/ccip/directory/testnet. Значення в `appsettings.example.json`
   відповідають Sepolia → Arbitrum Sepolia на момент написання.

### Крок 1. Компіляція контрактів

1. https://remix.ethereum.org → створити `CrossChainMessenger.sol` і `CrossChainReceiver.sol`.
   Remix сам підтягне `@chainlink/contracts-ccip` і OpenZeppelin v5 з npm.
   Локально в Hardhat/Foundry: `npm install @chainlink/contracts-ccip @openzeppelin/contracts`.
2. Solidity Compiler → `0.8.24+` → Compile.
3. З Compilation Details скопіювати `BYTECODE` (поле `object`) у ключ `bytecode` файлів
   `contracts/artifacts/CrossChainMessenger.json` та `CrossChainReceiver.json`.
   Порожнє значення `"0x"` клієнт відхилить з підказкою.

### Крок 2. Конфігурація

Скопіювати `appsettings.example.json` → `appsettings.json` (файл у `.gitignore`), заповнити:

- `Web3Settings:RpcUrl`, `ChainId` = `11155111`, `PrivateKey` — тестовий ключ (вихідна мережа);
- `Lab8Settings:Source:RouterAddress` та `LinkTokenAddress` — Router і LINK у Sepolia;
- `Lab8Settings:Destination` — RPC, `ChainId`, `ChainSelector`, `RouterAddress` цільової L2;
- за потреби `MessageText` і `LinkFundingAmount`.

### Крок 3. Відправка повідомлення (контрольне завдання)

```bash
dotnet restore
dotnet run
```

Сценарій:

1. розгортає `CrossChainReceiver` в Arbitrum Sepolia;
2. розгортає `CrossChainMessenger` в Ethereum Sepolia;
3. переказує LINK на баланс месенджера;
4. питає в Router-а комісію (`getFee`);
5. викликає `sendMessage`, дістає `messageId` з події `MessageSent` і друкує посилання на CCIP Explorer.

Адреси й останній `messageId` зберігаються у `deployment-state-lab8.json`; повторний запуск
не витрачає газ на повторний деплой і просто відправляє нове повідомлення.

### Крок 4. Агент очікування доставки

```bash
dotnet run -- --track
```

Агент кожні 15 с читає `lastMessageId` отримувача в L2 і порівнює з `messageId`
відправленого повідомлення. Тестова доставка зазвичай триває 10–30 хвилин, тому
дефолтний таймаут — 40 хвилин. Якщо між відправкою та `--track` було надіслано ще одне
повідомлення, трекер шукатиме саме останнє.

### Приклад виводу (скорочено, значення орієнтовні)

```
КРОК 4. РОЗРАХУНОК КОМІСІЇ (Router.getFee)
  Орієнтовна комісія....: 0.0412 LINK
КРОК 5. ВІДПРАВКА ПОВІДОМЛЕННЯ (sendMessage -> Router.ccipSend)
  Текст.................: Привіт з Ethereum Sepolia! Лаб. робота №8, Рубан Андрій, ПДМ-61
  Списано комісії.......: 0.0412 LINK
  messageId.............: 0x9f3a...c41e
  CCIP Explorer.........: https://ccip.chain.link/msg/0x9f3a...c41e

[12:04:19] Отримувач | повідомлень ще немає
[12:19:34] [УСПІХ] Повідомлення доставлено в цільову мережу.
  [OK] Status: Success — контракт-отримувач у цільовій мережі зберіг повідомлення.
```

---

## 4. Виконання контрольного завдання

| Вимога | Реалізація |
|---|---|
| Налаштувати L2-мережу (MetaMask, конфіг, крани) | Крок 0; `Lab8Settings:Destination`, `Web3Factory.DestinationClient` |
| Розгорнути `CrossChainMessenger` у Sepolia з адресами Router і LINK | `MessengerService.DeployAsync` → конструктор `(router_, link_)` |
| Розгорнути контракт-пустушку-отримувач у L2 | `CrossChainReceiver`, `ReceiverService.DeployAsync` |
| Поповнити баланс месенджера LINK | `ScenarioRunner.EnsureMessengerFundedAsync` (`LinkService.TransferAsync`) |
| Запустити скрипт, що викликає `sendMessage` | `MessengerService.SendMessageAsync`, розбір `MessageSent` → `messageId` |
| Підтвердження Status: Success | CCIP Explorer за `messageId` + агент `--track` |

**У звіті:** лістинги `CrossChainMessenger.sol`, `CrossChainReceiver.sol`, код клієнта
(`ScenarioRunner.cs`, `MessengerService.cs`, `DeliveryTracker.cs`), скриншот консолі
з `messageId` та скриншот CCIP Explorer зі статусом **Success** (Sepolia → Arbitrum Sepolia).

---

## 5. Контрольні запитання

**Поясніть різницю між підходами до масштабування Optimistic Rollups та Zero-Knowledge (ZK) Rollups.**

Обидва підходи виконують транзакції поза L1, а на L1 публікують стиснуті дані та новий стан,
але по-різному доводять його коректність. Optimistic Rollups (Arbitrum, Optimism, Base)
«оптимістично» вважають кожен пакет правильним. Протягом вікна оскарження (приблизно 7 днів)
будь-який спостерігач може подати fraud proof, і тоді некоректний стан відкочується.
Звідси затримка виведення коштів на L1, зате прості обчислення та повна EVM-еквівалентність.
ZK Rollups (zkSync, Scroll, Starknet) разом із пакетом публікують криптографічне доказове
свідчення валідності (SNARK/STARK), яке L1-контракт перевіряє одразу. Фіналізація на L1 швидка,
і довіра до чесності спостерігачів не потрібна. Ціна — складна й дорога генерація доказів
і складніша сумісність з EVM.

**Чому при розробці бекенд-сервісу для L2-мережі (наприклад, Arbitrum) не потрібно вивчати нові мови програмування або нові бібліотеки замість Nethereum чи ethers.js?**

EVM-сумісні L2 виконують той самий байткод, використовують той самий формат транзакцій,
ABI-кодування та JSON-RPC API (`eth_call`, `eth_sendRawTransaction`, `eth_getLogs`), що й L1.
Тому Nethereum, ethers.js чи go-ethereum працюють без змін. Саме це демонструє наш клієнт:
`Web3Factory` створює другий `Web3` лише з іншим `RpcUrl` і `ChainId`, а адреса акаунта та
сервіси залишаються тими самими. Нюанси є лише на рівні економіки й середовища (окрема
модель комісій із витратами на публікацію даних у L1, особливості `block.number`), але не
на рівні інструментів.

**Яку функцію виконує ідентифікатор цільової мережі (Destination Chain Selector) у протоколі CCIP, і чому не використовується стандартний Chain ID з EVM?**

Chain Selector — це `uint64`-ідентифікатор мережі, який присвоює сам CCIP. Разом із
ідентифікатором мережі-відправника він задає напрямок (lane), тобто пару Router-ів, між
якими ходять повідомлення, і за ним Router вирішує, чи підтримується такий маршрут.
Chain ID не підходить, бо це поняття лише EVM. CCIP працює й з не-EVM мережами (Solana,
Aptos), де EVM Chain ID не існує, а його унікальність у різних сімействах мереж не
гарантована. Єдиний простір селекторів, що контролюється протоколом, дає однозначну
й уніфіковану адресацію будь-яких блокчейнів і не залежить від змін Chain ID у форках.
Тому в `appsettings.json` є окремі `ChainId` (для підпису транзакцій у L2) і `ChainSelector`
(для CCIP).

**Чому крос-чейн транзакції вимагають сплати комісії не лише за виконання запиту в поточній мережі, але й окремої комісії для маршрутизатора (через токен LINK або додатковий нативний актив)?**

Користувач сплачує газ лише за транзакцію у вихідній мережі (`ccipSend`). Виконання
повідомлення в цільовій мережі ініціює не він, а мережа оракулів, яка надсилає там власну
транзакцію і сама витрачає газ. Оскільки в користувача немає транзакції в цільовій мережі,
вартість виконання потрібно передплатити у вихідній. Комісія CCIP складається з вартості газу
виконання в цільовій мережі (`gasLimit` × поточна ціна газу), винагороди децентралізованих
мереж оракулів, які підтверджують і виконують повідомлення (committing та executing DON,
Risk Management Network), та надбавки за обсяг даних. Router динамічно розраховує її через
`getFee`, а оплата йде в LINK або нативним активом. У нашому контракті комісія списується
з LINK-балансу контракту, тому його попередньо поповнюють.

---

## 6. Технологічний стек

| Шар | Технологія |
|---|---|
| Смарт-контракти | Solidity 0.8.24, OpenZeppelin v5 (Ownable, SafeERC20), Chainlink CCIP (`IRouterClient`, `CCIPReceiver`) |
| Мережі | Ethereum Sepolia (L1) → Arbitrum Sepolia (L2, Optimistic Rollup) |
| Крос-чейн протокол | Chainlink CCIP, оплата комісії в LINK |
| Клієнт та агент | C# (.NET 8), Nethereum 4.29, Microsoft.Extensions (DI, Options, Configuration) |
| Стан деплою | `deployment-state-lab8.json` |