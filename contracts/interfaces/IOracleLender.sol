// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

/// @notice Мінімальний інтерфейс кредитного протоколу, потрібний атакуючому.
interface IOracleLender {
    function depositCollateral(uint256 amount) external;
    function borrow(uint256 amount) external;
    function maxBorrowable(address user) external view returns (uint256);
}