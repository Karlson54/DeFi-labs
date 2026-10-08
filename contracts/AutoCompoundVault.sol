// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import "@openzeppelin/contracts/token/ERC20/ERC20.sol";
import "@openzeppelin/contracts/token/ERC20/IERC20.sol";
import "@openzeppelin/contracts/token/ERC20/extensions/IERC20Metadata.sol";
import "@openzeppelin/contracts/token/ERC20/utils/SafeERC20.sol";
import "@openzeppelin/contracts/utils/ReentrancyGuard.sol";
import "./interfaces/IUniswapV2Router02.sol";

/// @title AutoCompoundVault
/// @notice Сховище-агрегатор дохідності: приймає базовий актив, видає токени-акції (shares)
///         і через compound() конвертує накопичену винагороду в базовий актив
///         за допомогою Uniswap V2 Router (композитність, як у лабораторній №4).
/// @dev Саме сховище є ERC-20 токеном (vTokenA). Вартість акції = totalAssets / totalSupply,
///      тому реінвестування подорожчує акції ВСІХ власників без жодних дій з їхнього боку.
contract AutoCompoundVault is ERC20, ReentrancyGuard {
    using SafeERC20 for IERC20;

    // Адреси фіксуються при розгортанні -> immutable (економія газу, неможливість підміни).
    IERC20 public immutable asset;        // базовий актив (Токен A)
    IERC20 public immutable rewardToken;  // токен винагороди (Токен B)
    IUniswapV2Router02 public immutable router;

    // Захист від First Deposit Inflation Attack: ці акції при першому депозиті
    // назавжди "спалюються" на мертву адресу і ніколи не можуть бути викуплені.
    // (OpenZeppelin v5 забороняє _mint на address(0), тому використовуємо 0xdead.)
    uint256 public constant MINIMUM_SHARES = 1000;
    address public constant DEAD_ADDRESS = address(0xdead);

    event Deposit(address indexed user, uint256 assets, uint256 shares);
    event Withdraw(address indexed user, uint256 shares, uint256 assets);
    event Compounded(address indexed caller, uint256 rewardSold, uint256 assetsReceived);

    /// @param asset_ адреса базового активу
    /// @param rewardToken_ адреса токена винагороди
    /// @param router_ адреса Uniswap V2 Router02 (Dependency Injection: той самий код
    ///        працює у будь-якій мережі)
    constructor(address asset_, address rewardToken_, address router_)
    ERC20(
    string.concat("Vault ", IERC20Metadata(asset_).name()),
    string.concat("v", IERC20Metadata(asset_).symbol())
    )
    {
        require(asset_ != address(0) && rewardToken_ != address(0) && router_ != address(0), "AutoCompoundVault: zero address");
        require(asset_ != rewardToken_, "AutoCompoundVault: identical tokens");
        asset = IERC20(asset_);
        rewardToken = IERC20(rewardToken_);
        router = IUniswapV2Router02(router_);
    }

    // ---------------------------------------------------------------------
    // Математика акцій (view-функції)
    // ---------------------------------------------------------------------

    /// @notice Загальна кількість базового активу, що належить сховищу.
    /// @dev Токени винагороди сюди НЕ входять, доки compound() не обміняє їх на базовий актив.
    function totalAssets() public view returns (uint256) {
        return asset.balanceOf(address(this));
    }

    /// @notice Скільки акцій відповідає заданій кількості активів.
    /// @dev shares = assets * totalSupply / totalAssets. Ділення округлює вниз —
    ///      на користь сховища, а не того, хто вносить кошти.
    function convertToShares(uint256 assets) public view returns (uint256) {
        uint256 supply = totalSupply();
        return supply == 0 ? assets : (assets * supply) / totalAssets();
    }

    /// @notice Скільки активів можна отримати за задану кількість акцій.
    /// @dev assets = shares * totalAssets / totalSupply.
    function convertToAssets(uint256 shares) public view returns (uint256) {
        uint256 supply = totalSupply();
        return supply == 0 ? shares : (shares * totalAssets()) / supply;
    }

    // ---------------------------------------------------------------------
    // Депозит і зняття
    // ---------------------------------------------------------------------

    /// @notice Внесення базового активу в обмін на акції.
    /// @dev Потрібен попередній approve(vault, assets) на токені базового активу.
    ///      Порядок: розрахунок акцій -> _mint (зміна стану) -> transferFrom (зовнішній виклик),
    ///      тобто патерн Checks-Effects-Interactions + nonReentrant.
    function deposit(uint256 assets) external nonReentrant returns (uint256 shares) {
        require(assets > 0, "AutoCompoundVault: zero assets");

        uint256 supply = totalSupply();
        if (supply == 0) {
            // Перший депозит: курс 1:1, але MINIMUM_SHARES акцій спалюються назавжди.
            require(assets > MINIMUM_SHARES, "AutoCompoundVault: first deposit too small");
            shares = assets - MINIMUM_SHARES;
            _mint(DEAD_ADDRESS, MINIMUM_SHARES);
        } else {
            // totalAssets() рахується ДО надходження нових коштів — це важливо для формули.
            shares = (assets * supply) / totalAssets();
        }
        require(shares > 0, "AutoCompoundVault: zero shares");

        _mint(msg.sender, shares);
        asset.safeTransferFrom(msg.sender, address(this), assets);

        emit Deposit(msg.sender, assets, shares);
    }

    /// @notice Спалює акції та повертає пропорційну частку базового активу.
    /// @dev Зворотний порядок: _burn -> transfer (Checks-Effects-Interactions).
    function withdraw(uint256 shares) external nonReentrant returns (uint256 assets) {
        require(shares > 0, "AutoCompoundVault: zero shares");

        assets = convertToAssets(shares);
        require(assets > 0, "AutoCompoundVault: zero assets");

        _burn(msg.sender, shares); // revert, якщо в користувача недостатньо акцій
        asset.safeTransfer(msg.sender, assets);

        emit Withdraw(msg.sender, shares, assets);
    }

    // ---------------------------------------------------------------------
    // Автоматичне реінвестування
    // ---------------------------------------------------------------------

    /// @notice Обмінює ВСЮ накопичену винагороду на базовий актив і залишає його у сховищі.
    /// @dev Функція відкрита (external): її може викликати будь-який кіпер, а гроші
    ///      у будь-якому разі потрапляють лише на адресу сховища (to = address(this)).
    ///      Нові акції не емітуються -> зростає totalAssets() -> дорожчає кожна акція.
    function compound() external nonReentrant returns (uint256 assetsReceived) {
        uint256 rewardBalance = rewardToken.balanceOf(address(this));
        require(rewardBalance > 0, "AutoCompoundVault: no rewards");

        // Делегуємо Router-у право забрати винагороду (forceApprove — безпечне оновлення allowance).
        rewardToken.forceApprove(address(router), rewardBalance);

        address[] memory path = new address[](2);
        path[0] = address(rewardToken);
        path[1] = address(asset);

        // amountOutMin = 1 — навчальне спрощення (як у лабораторній №4). У продакшені
        // мінімум рахують від оракула/TWAP, інакше можливий sandwich-атак (MEV).
        uint256[] memory amounts = router.swapExactTokensForTokens(
            rewardBalance,
            1,
            path,
            address(this),            // отримувач — саме сховище, кошти змішуються з депозитами
            block.timestamp + 5 minutes
        );

        assetsReceived = amounts[amounts.length - 1];
        emit Compounded(msg.sender, rewardBalance, assetsReceived);
    }
}