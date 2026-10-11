// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

/// @title IPriceOracle
/// @notice Єдиний інтерфейс джерела ціни для кредитора. Завдяки йому той самий
///         кредитор можна підключити і до вразливого спот-оракула, і до захищеного.
interface IPriceOracle {
    /// @return ціна 1 токена A, виражена в токенах B (18 знаків)
    function getPrice() external view returns (uint256);
}