// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import "@openzeppelin/contracts/access/Ownable.sol";
import "./interfaces/IPriceOracle.sol";

/// @title TrustedPriceOracle
/// @notice ЗАХИЩЕНИЙ оракул-імітація децентралізованого фіда (Chainlink з лаб. №6).
/// @dev Ціна не залежить від стану жодного пулу, тому її не можна змінити всередині
///      атакуючої транзакції. Як і в лаб. №6, застаріла ціна відкидається (heartbeat).
///      На реальній мережі тут стоїть AggregatorV3Interface.latestRoundData().
contract TrustedPriceOracle is IPriceOracle, Ownable {
    uint256 public constant MAX_PRICE_AGE = 1 days;

    uint256 public price;
    uint256 public updatedAt;

    event PriceUpdated(uint256 oldPrice, uint256 newPrice);

    constructor(uint256 initialPrice_) Ownable(msg.sender) {
        require(initialPrice_ > 0, "TrustedPriceOracle: zero price");
        price = initialPrice_;
        updatedAt = block.timestamp;
    }

    function setPrice(uint256 newPrice) external onlyOwner {
        require(newPrice > 0, "TrustedPriceOracle: zero price");
        emit PriceUpdated(price, newPrice);
        price = newPrice;
        updatedAt = block.timestamp;
    }

    function getPrice() external view override returns (uint256) {
        require(block.timestamp <= updatedAt + MAX_PRICE_AGE, "TrustedPriceOracle: stale price");
        return price;
    }
}