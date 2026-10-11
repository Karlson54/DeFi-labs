// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

/// @notice Кредитор миттєвих позик (бік, що видає ліквідність).
interface IFlashLender {
    function flashLoan(address receiver, uint256 amount, bytes calldata params) external;
}

/// @notice Бік, що отримує миттєву позику: кредитор викликає його з коллбеком.
interface IFlashBorrower {
    function executeOperation(
        address token,
        uint256 amount,
        uint256 fee,
        address initiator,
        bytes calldata params
    ) external;
}