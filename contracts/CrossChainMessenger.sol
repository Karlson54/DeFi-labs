// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import "@openzeppelin/contracts/access/Ownable.sol";
import "@openzeppelin/contracts/token/ERC20/IERC20.sol";
import "@openzeppelin/contracts/token/ERC20/utils/SafeERC20.sol";
import "./CCIP.sol";

/// @title CrossChainMessenger
/// @notice Відправник міжмережевих повідомлень через Chainlink CCIP.
///         Комісію оракулів контракт сплачує нативним ETH зі свого балансу.
contract CrossChainMessenger is Ownable {
    using SafeERC20 for IERC20;

    IRouterClient public immutable router;
    // Збережено для сумісності конструктора; комісія в LINK більше не використовується.
    IERC20 public immutable linkToken;

    uint256 public gasLimit = 300_000;

    event MessageSent(
        bytes32 indexed messageId,
        uint64 indexed destinationChainSelector,
        address receiver,
        string text,
        address feeToken,
        uint256 fees
    );
    event GasLimitUpdated(uint256 oldLimit, uint256 newLimit);
    event LinkWithdrawn(address indexed to, uint256 amount);
    event EthWithdrawn(address indexed to, uint256 amount);

    constructor(address router_, address link_) Ownable(msg.sender) {
        require(router_ != address(0) && link_ != address(0), "CrossChainMessenger: zero address");
        router = IRouterClient(router_);
        linkToken = IERC20(link_);
    }

    /// @notice Контракт приймає ETH для оплати комісій.
    receive() external payable {}

    function getFee(uint64 destinationChainSelector, address receiver, string calldata text)
    external
    view
    returns (uint256)
    {
        return router.getFee(destinationChainSelector, _buildCCIPMessage(receiver, text));
    }

    function sendMessage(uint64 destinationChainSelector, address receiver, string calldata text)
    external
    onlyOwner
    returns (bytes32 messageId)
    {
        require(receiver != address(0), "CrossChainMessenger: zero receiver");
        require(bytes(text).length > 0, "CrossChainMessenger: empty text");

        Client.EVM2AnyMessage memory message = _buildCCIPMessage(receiver, text);

        uint256 fees = router.getFee(destinationChainSelector, message);
        require(address(this).balance >= fees, "CrossChainMessenger: not enough ETH");

        // Комісія передається Router-у разом із викликом (msg.value).
        messageId = router.ccipSend{value: fees}(destinationChainSelector, message);

        emit MessageSent(messageId, destinationChainSelector, receiver, text, address(0), fees);
    }

    function setGasLimit(uint256 newLimit) external onlyOwner {
        require(newLimit > 0, "CrossChainMessenger: zero gas limit");
        emit GasLimitUpdated(gasLimit, newLimit);
        gasLimit = newLimit;
    }

    function withdrawEth(address payable to) external onlyOwner {
        require(to != address(0), "CrossChainMessenger: zero address");
        uint256 balance = address(this).balance;
        require(balance > 0, "CrossChainMessenger: nothing to withdraw");
        (bool ok,) = to.call{value: balance}("");
        require(ok, "CrossChainMessenger: ETH transfer failed");
        emit EthWithdrawn(to, balance);
    }

    function withdrawLink(address to) external onlyOwner {
        require(to != address(0), "CrossChainMessenger: zero address");
        uint256 balance = linkToken.balanceOf(address(this));
        require(balance > 0, "CrossChainMessenger: nothing to withdraw");
        linkToken.safeTransfer(to, balance);
        emit LinkWithdrawn(to, balance);
    }

    function _buildCCIPMessage(address receiver, string calldata text)
    internal
    view
    returns (Client.EVM2AnyMessage memory)
    {
        return Client.EVM2AnyMessage({
            receiver: abi.encode(receiver),
            data: abi.encode(text),
            tokenAmounts: new Client.EVMTokenAmount[](0),
        // Оставь здесь ту же строку extraArgs, что у тебя сейчас (V1 или V2): она не менялась.
            extraArgs: Client._argsToBytes(
                Client.EVMExtraArgsV2({gasLimit: gasLimit, allowOutOfOrderExecution: true})
            ),
            feeToken: address(0) // address(0) = оплата нативним ETH
        });
    }
}