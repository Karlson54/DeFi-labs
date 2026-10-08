// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

/// @title IUniswapV2Router02
/// @notice Мінімальний інтерфейс маршрутизатора Uniswap V2 (з лабораторної №4):
///         "карта" зовнішнього контракту, за якою EVM формує коректний виклик.
///         Сховищу потрібен лише swapExactTokensForTokens; addLiquidity викликає C#-клієнт.
interface IUniswapV2Router02 {
    function addLiquidity(
        address tokenA,
        address tokenB,
        uint256 amountADesired,
        uint256 amountBDesired,
        uint256 amountAMin,
        uint256 amountBMin,
        address to,
        uint256 deadline
    ) external returns (uint256 amountA, uint256 amountB, uint256 liquidity);

    function swapExactTokensForTokens(
        uint256 amountIn,
        uint256 amountOutMin,
        address[] calldata path,
        address to,
        uint256 deadline
    ) external returns (uint256[] memory amounts);
}