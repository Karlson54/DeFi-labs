// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import "./interfaces/IPriceOracle.sol";
import "./interfaces/IDexPool.sol";

/// @title SpotPriceOracle
/// @notice ВРАЗЛИВИЙ оракул: ціна = поточне співвідношення резервів AMM-пулу.
/// @dev Резерви пулу можна змінити в межах однієї транзакції великим обміном
///      (за кошти миттєвої позики), тому така ціна не є надійним джерелом істини.
contract SpotPriceOracle is IPriceOracle {
    IDexPool public immutable pool;

    constructor(address pool_) {
        require(pool_ != address(0), "SpotPriceOracle: zero pool");
        pool = IDexPool(pool_);
    }

    function getPrice() external view override returns (uint256) {
        uint256 reserveA = pool.reserveA();
        require(reserveA > 0, "SpotPriceOracle: empty pool");
        // Множимо ПЕРЕД діленням, щоб не втратити дробову частину.
        return (pool.reserveB() * 1e18) / reserveA;
    }
}