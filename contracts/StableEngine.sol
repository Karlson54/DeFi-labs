// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import "@openzeppelin/contracts/access/Ownable.sol";
import "@openzeppelin/contracts/utils/ReentrancyGuard.sol";
import "./StableCoin.sol";

/// @title StableEngine
/// @notice Кредитне ядро: приймає ETH як заставу та випускає стейблкоїн під неї
///         з надмірним забезпеченням (Over-collateralization, CR = 150%).
/// @dev Ціна ETH/USD — навчальний мок-оракул, який адміністратор може змінювати
///      для симуляції ринку (у продакшені тут стоїть Chainlink).
contract StableEngine is Ownable, ReentrancyGuard {
    // ---------------------------------------------------------------------
    // Стан протоколу
    // ---------------------------------------------------------------------

    // Адреса стейблкоїна фіксується при розгортанні -> immutable (економія газу).
    StableCoin public immutable stablecoin;

    // EVM не має чисел з рухомою комою: усі USD-величини та ціна мають 18 знаків.
    uint256 public constant PRECISION = 1e18;

    // Мінімальний коефіцієнт забезпечення у відсотках: 150 => 1.5.
    uint256 public constant COLLATERALIZATION_RATIO = 150;
    uint256 public constant RATIO_DENOMINATOR = 100;

    // Мінімально допустимий Health Factor = 1.0 з урахуванням 18 десяткових знаків.
    uint256 public constant MIN_HEALTH_FACTOR = 1e18;

    // Мок-ціна 1 ETH у USD (18 знаків). Наприклад, 2000e18 = $2000.
    uint256 public mockEthUsdPrice;

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
    event PriceUpdated(uint256 oldPrice, uint256 newPrice);

    modifier moreThanZero(uint256 amount) {
        require(amount > 0, "StableEngine: amount must be > 0");
        _;
    }

    constructor(address stablecoin_, uint256 initialPrice_) Ownable(msg.sender) {
        require(stablecoin_ != address(0), "StableEngine: zero stablecoin");
        require(initialPrice_ > 0, "StableEngine: zero price");
        stablecoin = StableCoin(stablecoin_);
        mockEthUsdPrice = initialPrice_;
    }

    // ---------------------------------------------------------------------
    // Адміністрування мок-оракула
    // ---------------------------------------------------------------------

    /// @notice Зміна мок-ціни ETH/USD (симуляція падіння/зростання ринку).
    function setMockEthUsdPrice(uint256 newPrice) external onlyOwner {
        require(newPrice > 0, "StableEngine: zero price");
        emit PriceUpdated(mockEthUsdPrice, newPrice);
        mockEthUsdPrice = newPrice;
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
    /// @dev Патерн "оптимістичний борг + перевірка інваріанта": спочатку збільшуємо
    ///      борг, потім перевіряємо Health Factor. Якщо він < 1 — revert скасовує
    ///      всі зміни стану, і лише після успішної перевірки викликається зовнішній mint.
    function mintStablecoin(uint256 amount) external nonReentrant moreThanZero(amount) {
        stablecoinMinted[msg.sender] += amount;
        _revertIfHealthFactorIsBroken(msg.sender);

        stablecoin.mint(msg.sender, amount);
        emit StablecoinMinted(msg.sender, amount);
    }

    /// @notice Повернення (спалення) частини боргу.
    /// @dev Користувач заздалегідь робить approve(engine, amount) на токені стейблкоїна.
    ///      Рушій забирає токени собі (transferFrom) і спалює їх. Здоров'я позиції
    ///      від погашення лише зростає, тому перевірка HF тут не потрібна.
    function burnStablecoin(uint256 amount) external nonReentrant moreThanZero(amount) {
        require(stablecoinMinted[msg.sender] >= amount, "StableEngine: burn exceeds debt");

        // Checks-Effects-Interactions: спершу оновлюємо стан, потім зовнішні виклики.
        stablecoinMinted[msg.sender] -= amount;

        require(
            stablecoin.transferFrom(msg.sender, address(this), amount),
            "StableEngine: transferFrom failed"
        );
        stablecoin.burn(address(this), amount);

        emit StablecoinBurned(msg.sender, amount);
    }

    /// @notice Зняття частини застави назад на гаманець.
    /// @dev Зменшення застави може зробити позицію неплатоспроможною, тому ПІСЛЯ
    ///      оновлення стану обов'язково викликається _revertIfHealthFactorIsBroken.
    function withdrawCollateral(uint256 amount) external nonReentrant moreThanZero(amount) {
        require(collateralDeposited[msg.sender] >= amount, "StableEngine: withdraw exceeds collateral");

        collateralDeposited[msg.sender] -= amount;
        _revertIfHealthFactorIsBroken(msg.sender);

        emit CollateralWithdrawn(msg.sender, amount);

        (bool ok, ) = msg.sender.call{value: amount}("");
        require(ok, "StableEngine: ETH transfer failed");
    }

    // ---------------------------------------------------------------------
    // Фінансова математика (view-функції)
    // ---------------------------------------------------------------------

    /// @notice Вартість застави користувача в USD (18 знаків).
    /// @dev collateralUsd = collateralWei * price / 1e18 — множимо перед діленням,
    ///      щоб не втратити дробову частину.
    function getCollateralValueInUsd(address user) public view returns (uint256) {
        return (collateralDeposited[user] * mockEthUsdPrice) / PRECISION;
    }

    /// @notice Health Factor позиції: HF = (Collateral * 100 / CR) / Debt, масштаб 1e18.
    /// @dev Якщо боргу немає — повертаємо максимальне число (позиція абсолютно безпечна).
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

        // Приводимо вартість застави до порогового значення забезпечення.
        uint256 collateralAdjusted = (collateralUsd * RATIO_DENOMINATOR) / COLLATERALIZATION_RATIO;
        return (collateralAdjusted * PRECISION) / debt;
    }

    /// @dev Інваріант безпеки: протокол ніколи не допускає позицію з HF < 1.
    function _revertIfHealthFactorIsBroken(address user) internal view {
        require(getHealthFactor(user) >= MIN_HEALTH_FACTOR, "StableEngine: health factor broken");
    }
}