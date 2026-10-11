// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import "@openzeppelin/contracts/token/ERC20/ERC20.sol";

/// @title AssetToken
/// @notice Власний ERC-20 актив, який використовується для тестування інтеграції.
contract AssetToken is ERC20 {
    constructor(string memory name_, string memory symbol_, uint256 initialSupply_)
    ERC20(name_, symbol_)
    {
        _mint(msg.sender, initialSupply_);
    }
}