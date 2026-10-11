// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import "@openzeppelin/contracts/access/Ownable.sol";

/// @title MockProtocol
/// @notice Протокол-мішень для DAO: має публічну комісію, яку може змінювати лише власник.
/// @dev Після transferOwnership(DAO) власником стає смарт-контракт SimpleDAO,
///      тож змінити комісію може лише результат успішного голосування.
contract MockProtocol is Ownable {
    // Комісія у відсотках; верхня межа захищає від абсурдних значень.
    uint256 public constant MAX_FEE = 100;

    uint256 public fee;

    event FeeUpdated(address indexed by, uint256 oldFee, uint256 newFee);

    constructor(uint256 initialFee_) Ownable(msg.sender) {
        require(initialFee_ <= MAX_FEE, "MockProtocol: fee too high");
        fee = initialFee_;
    }

    function setFee(uint256 newFee) external onlyOwner {
        require(newFee <= MAX_FEE, "MockProtocol: fee too high");
        emit FeeUpdated(msg.sender, fee, newFee);
        fee = newFee;
    }
}