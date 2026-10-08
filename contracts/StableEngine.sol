// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import "@openzeppelin/contracts/access/Ownable.sol";
import "@openzeppelin/contracts/utils/ReentrancyGuard.sol";
// Офіційний інтерфейс Chainlink Data Feed (npm install @chainlink/contracts).
// Для версій пакета 0.x шлях був іншим: src/v0.8/interfaces/AggregatorV3Interface.sol
import "@chainlink/contracts/src/v0.8/shared/interfaces/AggregatorV3Interface.sol";
import "./StableCoin.sol";

/// @title StableEngine
/// @notice Кредитне ядро: приймає ETH як заставу та випускає стейблкоїн під неї
///         з надмірним забезпеченням (CR = 150%). Ціна ETH береться з Chainlink,
///         а неплатоспроможні позиції можуть ліквідувати зовнішні агенти (боти).
contract StableEngine is Ownable, ReentrancyGuard {
    // ---------------------------------------------------------------------
    // Стан протоколу
    // ---------------------------------------------------------------------

    // Адреси стейблкоїна та оракула фіксуються при розгортанні -> immutable:
    // значення "запікаються" в байт-код, і читання не коштує газу SLOAD.
    StableCoin public immutable stablecoin;
    AggregatorV3Interface public immutable priceFeed;

    // EVM не має чисел з рухомою комою: усі USD-величини мають 18 знаків.
    uint256 public constant PRECISION = 1e18;

    // Chainlink-канали ETH/USD повертають ціну з 8 знаками, а наша математика
    // працює з 18, тому множимо на 10^(18 - 8) = 1e10.
    uint256 public constant FEED_PRECISION_MULTIPLIER = 1e10;

    // Максимальний вік ціни оракула. У продакшені = heartbeat каналу + запас.
    // Захист від "завислого" оракула: застарілу ціну контракт не використовує.
    uint256 public constant MAX_PRICE_AGE = 1 days;

    // Мінімальний коефіцієнт забезпечення у відсотках: 150 => 1.5.
    uint256 public constant COLLATERALIZATION_RATIO = 150;
    uint256 public constant RATIO_DENOMINATOR = 100;

    // Премія ліквідатора у відсотках від вартості погашеного боргу.
    uint256 public constant LIQUIDATION_BONUS = 10;

    // Мінімально допустимий Health Factor = 1.0 (18 знаків).
    uint256 public constant MIN_HEALTH_FACTOR = 1e18;

    // Децентралізований реєстр позицій кожного позичальника.
    mapping(address => uint256) public collateralDeposited; // застава в wei
    mapping(address => uint256) public stablecoinMinted;    // борг у стейблкоїнах (18 знаків)

    // ---------------------------------------------------------------------
    // Події (для індексації бекенд-сервісами, як у лабораторній №3)
    // ---------------------------------------------------------------------

    event CollateralDeposited(address indexed user, uint256 amount);
    event CollateralWithdrawn(address indexed user, uint256 amount);
    event StablecoinMinted(address indexed user, uint256 amount);
    event StablecoinBurned(address indexed user, uint256 amount);
    event Liquidated(address indexed user, address indexed liquidator, uint256 debtCovered, uint256 collateralSeized);
    event CollateralForceReduced(address indexed user, uint256 amount);

    modifier moreThanZero(uint256 amount) {
        require(amount > 0, "StableEngine: amount must be > 0");
        _;
    }

    /// @param stablecoin_ адреса токена стейблкоїна
    /// @param priceFeed_ адреса Chainlink Data Feed ETH/USD
    ///        (Sepolia: 0x694AA1769357215DE4FAC081bf1f309aDC325306)
    constructor(address stablecoin_, address priceFeed_) Ownable(msg.sender) {
        require(stablecoin_ != address(0), "StableEngine: zero stablecoin");
        require(priceFeed_ != address(0), "StableEngine: zero price feed");
        stablecoin = StableCoin(stablecoin_);
        priceFeed = AggregatorV3Interface(priceFeed_);
    }

    // ---------------------------------------------------------------------
    // Основні функції користувача
    // ---------------------------------------------------------------------

    /// @notice Внесення ETH як застави.
    /// @dev payable: нативна монета приходить через msg.value, тому approve не потрібен.
    function depositCollateral() external payable moreThanZero(msg.value) {
        collateralDeposited[msg.sender] += msg.value;
        emit CollateralDeposited(msg.sender, msg.value);
    }

    /// @notice Емісія стейблкоїна під заставу.
    /// @dev Оптимістичний підхід: спочатку збільшуємо борг, потім перевіряємо HF.
    ///      Якщо HF < 1 — revert скасовує всі зміни, і лише після успішної
    ///      перевірки викликається зовнішній mint.
    function mintStablecoin(uint256 amount) external nonReentrant moreThanZero(amount) {
        stablecoinMinted[msg.sender] += amount;
        _revertIfHealthFactorIsBroken(msg.sender);

        stablecoin.mint(msg.sender, amount);
        emit StablecoinMinted(msg.sender, amount);
    }

    /// @notice Повернення (спалення) частини боргу.
    /// @dev Користувач заздалегідь робить approve(engine, amount) на токені.
    function burnStablecoin(uint256 amount) external nonReentrant moreThanZero(amount) {
        require(stablecoinMinted[msg.sender] >= amount, "StableEngine: burn exceeds debt");

        // Checks-Effects-Interactions: спершу стан, потім зовнішні виклики.
        stablecoinMinted[msg.sender] -= amount;

        require(
            stablecoin.transferFrom(msg.sender, address(this), amount),
            "StableEngine: transferFrom failed"
        );
        stablecoin.burn(address(this), amount);

        emit StablecoinBurned(msg.sender, amount);
    }

    /// @notice Зняття частини застави назад на гаманець.
    /// @dev ПІСЛЯ зменшення застави обов'язково перевіряється Health Factor.
    function withdrawCollateral(uint256 amount) external nonReentrant moreThanZero(amount) {
        require(collateralDeposited[msg.sender] >= amount, "StableEngine: withdraw exceeds collateral");

        collateralDeposited[msg.sender] -= amount;
        _revertIfHealthFactorIsBroken(msg.sender);

        emit CollateralWithdrawn(msg.sender, amount);

        (bool ok, ) = msg.sender.call{value: amount}("");
        require(ok, "StableEngine: ETH transfer failed");
    }

    // ---------------------------------------------------------------------
    // Примусова ліквідація
    // ---------------------------------------------------------------------

    /// @notice Ліквідація неплатоспроможної позиції (HF < 1) зовнішнім агентом.
    /// @dev Ліквідатор погашає ВЕСЬ борг позичальника власними стейблкоїнами
    ///      (потрібен approve(engine, debt)) і отримує заставу на суму боргу
    ///      плюс LIQUIDATION_BONUS відсотків. Якщо застави недостатньо для
    ///      виплати з бонусом — віддається все, що є (безнадійний борг лягає на протокол).
    function liquidate(address user) external nonReentrant {
        require(user != msg.sender, "StableEngine: self liquidation");

        uint256 debt = stablecoinMinted[user];
        require(debt > 0, "StableEngine: no debt");
        require(getHealthFactor(user) < MIN_HEALTH_FACTOR, "StableEngine: position is healthy");

        // Обсяг ETH, що точно покриває номінал боргу за поточним курсом оракула.
        uint256 price = getEthUsdPrice();
        uint256 baseCollateral = (debt * PRECISION) / price;

        // До нього додається премія ліквідатора.
        uint256 collateralToSeize =
            (baseCollateral * (RATIO_DENOMINATOR + LIQUIDATION_BONUS)) / RATIO_DENOMINATOR;

        uint256 available = collateralDeposited[user];
        if (collateralToSeize > available) {
            collateralToSeize = available;
        }

        // Checks-Effects-Interactions: спочатку оновлюємо реєстр...
        stablecoinMinted[user] = 0;
        collateralDeposited[user] = available - collateralToSeize;

        // ...потім забираємо й спалюємо стейблкоїни ліквідатора...
        require(
            stablecoin.transferFrom(msg.sender, address(this), debt),
            "StableEngine: transferFrom failed"
        );
        stablecoin.burn(address(this), debt);

        emit Liquidated(user, msg.sender, debt, collateralToSeize);

        // ...і лише наприкінці переказуємо заставу ліквідатору.
        (bool ok, ) = msg.sender.call{value: collateralToSeize}("");
        require(ok, "StableEngine: ETH transfer failed");
    }

    /// @notice ЛИШЕ ДЛЯ ЛАБОРАТОРНОЇ. Аналогів у реальних протоколах немає!
    /// @dev Примусово зменшує заставу в реєстрі без виведення ETH і без жодних
    ///      перевірок безпеки. Дає змогу штучно створити неплатоспроможність,
    ///      бо справжню ціну Chainlink на тестовій мережі змінити не можна.
    ///      У продакшені така функція — це бекдор: її необхідно видалити.
    function simulateInsolvency(address user, uint256 amount) external onlyOwner {
        require(amount <= collateralDeposited[user], "StableEngine: amount exceeds collateral");
        collateralDeposited[user] -= amount;
        emit CollateralForceReduced(user, amount);
    }

    // ---------------------------------------------------------------------
    // Оракул та фінансова математика (view-функції)
    // ---------------------------------------------------------------------

    /// @notice Ціна 1 ETH у USD з 18 знаками, отримана з Chainlink.
    /// @dev Нормалізація: 8 знаків оракула * 1e10 = 18 знаків. Додатково перевіряємо,
    ///      що ціна додатна й не застаріла. Без цього "завислий" оракул дозволив би
    ///      випускати борг під давно неактуальну вартість застави.
    function getEthUsdPrice() public view returns (uint256) {
        (, int256 answer, , uint256 updatedAt, ) = priceFeed.latestRoundData();
        require(answer > 0, "StableEngine: invalid oracle price");
        require(
            updatedAt != 0 && block.timestamp <= updatedAt + MAX_PRICE_AGE,
            "StableEngine: stale oracle price"
        );
        return uint256(answer) * FEED_PRECISION_MULTIPLIER;
    }

    /// @notice Вартість застави користувача в USD (18 знаків).
    /// @dev Множимо ПЕРЕД діленням, щоб не втратити дробову частину.
    function getCollateralValueInUsd(address user) public view returns (uint256) {
        return (collateralDeposited[user] * getEthUsdPrice()) / PRECISION;
    }

    /// @notice Health Factor: HF = (Collateral * 100 / CR) / Debt, масштаб 1e18.
    /// @dev Якщо боргу немає — повертаємо максимальне число.
    function getHealthFactor(address user) public view returns (uint256) {
        return _calculateHealthFactor(stablecoinMinted[user], getCollateralValueInUsd(user));
    }

    /// @notice Скільки стейблкоїнів користувач ще може випустити до межі HF = 1.
    function getMaxMintable(address user) external view returns (uint256) {
        uint256 maxDebt = (getCollateralValueInUsd(user) * RATIO_DENOMINATOR) / COLLATERALIZATION_RATIO;
        uint256 debt = stablecoinMinted[user];
        return maxDebt > debt ? maxDebt - debt : 0;
    }

    function _calculateHealthFactor(uint256 debt, uint256 collateralUsd) internal pure returns (uint256) {
        if (debt == 0) return type(uint256).max;

        uint256 collateralAdjusted = (collateralUsd * RATIO_DENOMINATOR) / COLLATERALIZATION_RATIO;
        return (collateralAdjusted * PRECISION) / debt;
    }

    /// @dev Інваріант безпеки: протокол ніколи не допускає позицію з HF < 1.
    function _revertIfHealthFactorIsBroken(address user) internal view {
        require(getHealthFactor(user) >= MIN_HEALTH_FACTOR, "StableEngine: health factor broken");
    }
}