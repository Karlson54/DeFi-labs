// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import "@openzeppelin/contracts/token/ERC20/IERC20.sol";
import "./interfaces/IUniswapV2Router02.sol";

/// @title DefiIntegrator
/// @notice Демонструє композитність (Composability): наш контракт викликає
///         функції стороннього AMM-протоколу (Uniswap V2 Router) від імені
///         користувача, не переписуючи власну математику пулу.
contract DefiIntegrator {
    // Адреса Router-а фіксується при розгортанні -> immutable (економія газу,
    // значення "запікається" прямо в байт-код).
    IUniswapV2Router02 public immutable router;

    /// @param router_ адреса офіційного Router-контракту обраного DEX
    ///        (Dependency Injection: контракт стає універсальним — той самий
    ///        код працює і в Mainnet, і в тестовій мережі).
    constructor(address router_) {
        require(router_ != address(0), "DefiIntegrator: zero router");
        router = IUniswapV2Router02(router_);
    }

    /// @notice Делеговане додавання ліквідності у зовнішній пул Uniswap V2.
    /// @dev Router приймає кошти лише від безпосереднього викликача — тому
    ///      спочатку токени переміщуються з msg.sender на цей контракт
    ///      (потребує попереднього approve(integrator, amount) від користувача),
    ///      а вже потім контракт сам дає approve Router-у.
    function provideLiquidity(
        address tokenA,
        address tokenB,
        uint256 amountADesired,
        uint256 amountBDesired
    ) external returns (uint256 liquidity) {
        require(IERC20(tokenA).transferFrom(msg.sender, address(this), amountADesired), "DefiIntegrator: transferFrom A failed");
        require(IERC20(tokenB).transferFrom(msg.sender, address(this), amountBDesired), "DefiIntegrator: transferFrom B failed");

        require(IERC20(tokenA).approve(address(router), amountADesired), "DefiIntegrator: approve A failed");
        require(IERC20(tokenB).approve(address(router), amountBDesired), "DefiIntegrator: approve B failed");

        // amountAMin/amountBMin = 1 — навчальне спрощення. У продакшені їх
        // рахують динамічно від поточного курсу, щоб уникнути MEV/фронтраннінгу.
        (, , liquidity) = router.addLiquidity(
            tokenA,
            tokenB,
            amountADesired,
            amountBDesired,
            1,
            1,
            msg.sender,            // LP-токени йдуть напряму користувачу, не застрягають тут
            block.timestamp + 5 minutes // deadline: захист від виконання застарілої tx майнером
        );
    }

    /// @notice Обмін токена через зовнішній Router (програмний трейдинг).
    function swapTokens(
        address tokenIn,
        address tokenOut,
        uint256 amountIn,
        uint256 amountOutMin
    ) external returns (uint256 amountOut) {
        require(IERC20(tokenIn).transferFrom(msg.sender, address(this), amountIn), "DefiIntegrator: transferFrom failed");
        require(IERC20(tokenIn).approve(address(router), amountIn), "DefiIntegrator: approve failed");

        address[] memory path = new address[](2);
        path[0] = tokenIn;
        path[1] = tokenOut;

        uint256[] memory amounts = router.swapExactTokensForTokens(
            amountIn,
            amountOutMin,     // захист від проковзування: менше — Revert усієї tx
            path,
            msg.sender,        // вихідні токени йдуть напряму користувачу
            block.timestamp + 5 minutes
        );

        amountOut = amounts[amounts.length - 1];
    }
}