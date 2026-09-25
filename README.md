# Лабораторна робота №3 — Full-stack Web3-інфраструктура

**Тема.** Розробка бекенд-індексатора блокчейн-подій та клієнтського інтерфейсу для DeFi-пулу.
**Виконав:** Рубан Андрій, група ПДМ-61.

Бекенд — ASP.NET Core (.NET 8) + Nethereum + Entity Framework Core (MSSQL).
Фронтенд — React (Vite) + ethers.js.

---

## 1. Архітектура
Смарт-контракт (DexPool.sol)
│ emit Swap(...)
▼
Локальна нода (Hardhat)
│ eth_getLogs (поллінг кожні 5с)
▼
SwapIndexerService (BackgroundService, Nethereum)
│ EF Core
▼
MSSQL (SwapRecords, Checkpoints)
│ GET /api/swaps?trader=...
▼
REST API (ASP.NET Core Minimal API)
│ fetch
▼
React SPA (MetaMask + таблиця історії)


Ідея: блокчейн не призначений для складних запитів з фільтрацією й пагінацією. Індексатор один раз читає логи подій і зберігає їх у звичайну реляційну БД, а фронтенд ходить за даними вже туди — швидко, з фільтрацією по конкретному трейдеру.

---

## 2. Структура проєкту
```
DeFi-labs/
├── contracts/
│ ├── AssetToken.sol # ERC-20 актив (успадкований з лабораторної №2)
│ ├── DexPool.sol # AMM-пул, емітує подію Swap
│ └── artifacts/
│ ├── AssetToken.json # abi + bytecode для деплою через Nethereum
│ └── DexPool.json
├── Models/
│ ├── Web3Settings.cs # конфіг RPC-вузла та приватного ключа
│ ├── DeploymentState.cs # адреси розгорнутих контрактів
│ ├── IndexerSettings.cs # конфіг індексатора + CORS
│ ├── SwapRecordEntity.cs # EF Core сутності (SwapRecord, Checkpoint)
│ └── Contracts/
│ └── SwapEventDto.cs # DTO для декодування події Swap
├── Services/
│ ├── Web3Factory.cs # створення Web3-клієнта з підписувачем
│ ├── DeploymentStateStore.cs # читання/запис deployment-state.json
│ └── SwapIndexerService.cs # фоновий поллінг подій Swap
├── Data/
│ └── SwapIndexerDbContext.cs # EF Core DbContext (MSSQL)
├── frontend/ # React SPA (Vite)
│ ├── package.json
│ ├── vite.config.js
│ ├── index.html
│ └── src/
│ ├── main.jsx
│ └── App.jsx
├── Program.cs # DI, веб-хост, REST API, допоміжні режими
├── appsettings.json
├── appsettings.example.json
├── DeFi.csproj
├── .gitignore
└── deployment-state.json # адреси контрактів (генерується автоматично)
```

---

## 3. Запуск

### Крок 0. Локальна нода

```bash
cd ~/hardhat-node
npx hardhat node
```

Залиште це вікно відкритим на весь час роботи. Перезапуск ноди обнуляє блокчейн — усі контракти й історію свопів доведеться розгортати заново (див. розділ 5).

### Крок 1. Налаштування

Скопіюйте `appsettings.example.json` → `appsettings.json`, заповніть:

- `Web3Settings:PrivateKey` — тестовий ключ першого акаунта Hardhat (`0xac09...ff80`, друкується самою нодою при старті).
- `Web3Settings:RpcUrl` — `http://127.0.0.1:8545`, `ChainId` — `31337`.
- `ConnectionStrings:SwapIndexer` — рядок підключення до вашого MSSQL-контейнера.
- `CorsSettings:AllowedOrigin` — `http://localhost:5173` (порт фронтенду).

### Крок 2. Розгортання контрактів (якщо ще не розгорнуті)

```bash
dotnet run -- --deploy
```

Деплоїть `AssetToken` (двічі — токен A і B), `DexPool`, вносить ліквідність 1000:2000, зберігає адреси у `deployment-state.json`.

### Крок 3. Індексатор + REST API

```bash
dotnet run
```

Піднімає веб-хост на `http://localhost:5000`, фоновий сервіс `SwapIndexerService` починає поллінг подій `Swap` кожні 5 секунд.

### Крок 4. Демонстраційний своп (опційно)

В окремому терміналі, **не зупиняючи** індексатор:

```bash
dotnet run -- --swap-once
# або з іншою сумою:
dotnet run -- --swap-once --amount 25
```

Через кілька секунд нова подія з'явиться в БД і автоматично підтягнеться на фронтенді.

### Крок 5. Фронтенд

```bash
cd frontend
npm install
npm run dev
```

Відкрити `http://localhost:5173`, підключити MetaMask:
- мережа: `Localhost 8545`, RPC `http://127.0.0.1:8545`, Chain ID `31337`;
- акаунт: імпортувати той самий тестовий приватний ключ, що вказаний у `Web3Settings:PrivateKey`.

---

## 4. Перевірка API напряму

```bash
curl http://localhost:5000/api/health
curl "http://localhost:5000/api/swaps?trader=0xf39fd6e51aad88f6f4ce6ab8827279cfffb9226"
```

---

## 5. Якщо нода Hardhat була перезапущена

Блокчейн обнулився — старі адреси контрактів і записи в БД більше не актуальні.

1. Розгорнути контракти заново:
```bash
   dotnet run -- --deploy
```
2. Очистити застарілий чекпоінт і старі записи у MSSQL:
```sql
   DELETE FROM Checkpoints;
   DELETE FROM SwapRecords;
```
3. Перезапустити індексатор:
```bash
   dotnet run
```

---

## 6. Контрольні запитання

**Чому Go вважають "нативною" мовою екосистеми Ethereum?**

Головний клієнт мережі — Geth (Go-Ethereum) — написаний на Go. Розробляючи бекенд на Go, розробник імпортує ті самі бібліотеки, на яких працює сама нода, включно з утилітою `abigen`, що автоматично генерує код для виклику смарт-контрактів.

**Яку архітектурну проблему вирішує зберігання блокчейн-подій у реляційній БД замість прямих запитів з фронтенду?**

Прямі запити до RPC-вузла для отримання історичних даних працюють повільно, обмежуються rate limit провайдерів і не підтримують класичної фільтрації та пагінації. Індексатор один раз читає логи подій і зберігає їх у SQL, а REST API віддає швидкі вибірки з фільтрацією по конкретному трейдеру.

**Що означає ключове слово `indexed` у декларації події смарт-контракту, і де фізично зберігаються ці параметри?**

Індексовані параметри (у `Swap` це `trader` і `tokenIn`) потрапляють у `topics` логу транзакції (до трьох індексованих полів плюс сама сигнатура події), що дозволяє нодам фільтрувати логи по них на рівні RPC без розбору всього `data`. Неіндексовані поля (`amountIn`, `amountOut`, `feeBps`) лежать у `data` і вимагають ABI-декодування.

**Чим HTTP Polling відрізняється від підключення через WebSocket?**

Поллінг (використаний у `SwapIndexerService`) періодично надсилає запит `eth_getLogs` за діапазон блоків — простий у реалізації, але з затримкою в середньому в половину інтервалу опитування (тут — 5с) і зайвим навантаженням на вузол при відсутності нових подій. WebSocket-підписка (`eth_subscribe`) тримає постійне з'єднання й отримує сповіщення про нові логи майже миттєво, але потребує провайдера з підтримкою WS та стійкого довготривалого з'єднання.

---

## 7. Технологічний стек

| Шар | Технологія |
|---|---|
| Смарт-контракти | Solidity 0.8.24, OpenZeppelin ERC-20 |
| Локальна мережа | Hardhat Network |
| Бекенд | ASP.NET Core 8 (Minimal API), Nethereum |
| Фоновий індексатор | `BackgroundService`, поллінг `eth_getLogs` |
| БД | MSSQL Server (Docker), Entity Framework Core |
| Фронтенд | React 18, Vite, ethers.js v6 |
| Гаманець | MetaMask |