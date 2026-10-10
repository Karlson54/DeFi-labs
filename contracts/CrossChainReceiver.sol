// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import "./CCIP.sol";

/// @title CrossChainReceiver
/// @notice Контракт-пустушка в цільовій L2-мережі (Arbitrum Sepolia): приймає повідомлення
///         від CCIP Router і зберігає останнє з них, щоб клієнт міг підтвердити доставку.
/// @dev Базовий CCIPReceiver гарантує, що ccipReceive() може викликати лише Router
///      (модифікатор onlyRouter), тому підробити повідомлення напряму неможливо.
///      У продакшені тут додають allowlist мереж-відправників і адрес-відправників.
contract CrossChainReceiver is CCIPReceiver {
    bytes32 public lastMessageId;
    uint64 public lastSourceChainSelector;
    address public lastSender;
    string public lastText;

    event MessageReceived(
        bytes32 indexed messageId,
        uint64 indexed sourceChainSelector,
        address sender,
        string text
    );

    /// @param router_ адреса CCIP Router у ЦІЛЬОВІЙ мережі
    constructor(address router_) CCIPReceiver(router_) {}

    /// @dev Викликається базовим контрактом після перевірки, що відправник — Router.
    function _ccipReceive(Client.Any2EVMMessage memory message) internal override {
        lastMessageId = message.messageId;
        lastSourceChainSelector = message.sourceChainSelector;
        // Відправник у CCIP — це байтовий масив (abi.encode(address) для EVM).
        lastSender = abi.decode(message.sender, (address));
        lastText = abi.decode(message.data, (string));

        emit MessageReceived(lastMessageId, lastSourceChainSelector, lastSender, lastText);
    }
}