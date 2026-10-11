// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

/// @title IDexPool
/// @notice Мінімальна "карта" пулу DexPool з лаб. №3 (резерви та обмін B -> A).
interface IDexPool {
    function reserveA() external view returns (uint256);
    function reserveB() external view returns (uint256);
    function swapBForA(uint256 amountIn, uint256 minAmountOut) external returns (uint256);
}