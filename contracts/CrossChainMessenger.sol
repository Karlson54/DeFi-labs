// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import "@openzeppelin/contracts/access/Ownable.sol";
import "@openzeppelin/contracts/token/ERC20/IERC20.sol";
import "@openzeppelin/contracts/token/ERC20/utils/SafeERC20.sol";
import "./CCIP.sol";

/// @title CrossChainMessenger
/// @notice Відправник міжмережевих текстових повідомлень через Chainlink CCIP.
///         Контракт розгортається у вихідній мережі (Ethereum Sepolia), а комісію
///         оракулів сплачує зі свого балансу в LINK.
/// @dev Крос-чейн виклик асинхронний: ccipSend лише ставить повідомлення в чергу,
///      а виконання в цільовій мережі відбувається через кілька хвилин.
///      Тому подія MessageSent з messageId потрібна для відстеження статусу доставки.
contract CrossChainMessenger is Ownable {
    using SafeERC20 for IERC20;

    // Адреси фіксуються при розгортанні -> immutable (економія газу, неможливість підміни).
    IRouterClient public immutable router;
    IERC20 public immutable linkToken;

    // Ліміт газу, який оракули виділяють на виконання повідомлення в цільовій мережі.
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

    /// @param router_ адреса CCIP Router у вихідній мережі
    /// @param link_ адреса токена LINK, яким сплачується комісія
    constructor(address router_, address link_) Ownable(msg.sender) {
        require(router_ != address(0) && link_ != address(0), "CrossChainMessenger: zero address");
        router = IRouterClient(router_);
        linkToken = IERC20(link_);
    }

    /// @notice Вартість доставки повідомлення в LINK (view, газ не витрачається).
    /// @dev Router рахує комісію динамічно: залежить від ціни газу в цільовій мережі.
    function getFee(uint64 destinationChainSelector, address receiver, string calldata text)
    external
    view
    returns (uint256)
    {
        return router.getFee(destinationChainSelector, _buildCCIPMessage(receiver, text));
    }

    /// @notice Відправка текстового повідомлення в іншу мережу.
    /// @dev Лише власник: комісія списується з LINK-балансу КОНТРАКТУ,
    ///      тож без onlyOwner будь-хто міг би спустошити баланс.
    /// @param destinationChainSelector CCIP-ідентифікатор цільової мережі (не EVM Chain ID!)
    /// @param receiver адреса контракту-отримувача в цільовій мережі
    /// @param text текст повідомлення
    function sendMessage(uint64 destinationChainSelector, address receiver, string calldata text)
    external
    onlyOwner
    returns (bytes32 messageId)
    {
        require(receiver != address(0), "CrossChainMessenger: zero receiver");
        require(bytes(text).length > 0, "CrossChainMessenger: empty text");

        Client.EVM2AnyMessage memory message = _buildCCIPMessage(receiver, text);

        // Динамічний розрахунок комісії безпосередньо перед відправкою.
        uint256 fees = router.getFee(destinationChainSelector, message);
        require(linkToken.balanceOf(address(this)) >= fees, "CrossChainMessenger: not enough LINK");

        // Делегуємо Router-у право списати рівно потрібну суму (forceApprove — безпечне оновлення allowance).
        linkToken.forceApprove(address(router), fees);

        messageId = router.ccipSend(destinationChainSelector, message);

        emit MessageSent(messageId, destinationChainSelector, receiver, text, address(linkToken), fees);
    }

    /// @notice Зміна ліміту газу для виконання в цільовій мережі.
    function setGasLimit(uint256 newLimit) external onlyOwner {
        require(newLimit > 0, "CrossChainMessenger: zero gas limit");
        emit GasLimitUpdated(gasLimit, newLimit);
        gasLimit = newLimit;
    }

    /// @notice Повернення невикористаних LINK власнику.
    function withdrawLink(address to) external onlyOwner {
        require(to != address(0), "CrossChainMessenger: zero address");
        uint256 balance = linkToken.balanceOf(address(this));
        require(balance > 0, "CrossChainMessenger: nothing to withdraw");

        linkToken.safeTransfer(to, balance);
        emit LinkWithdrawn(to, balance);
    }

    /// @dev Пакування даних у формат CCIP. CCIP розрахований і на не-EVM мережі (Solana тощо),
    ///      тому адреса отримувача й дані кодуються через abi.encode у байтовий масив.
    function _buildCCIPMessage(address receiver, string calldata text)
    internal
    view
    returns (Client.EVM2AnyMessage memory)
    {
        return Client.EVM2AnyMessage({
            receiver: abi.encode(receiver),
            data: abi.encode(text),
        // Передаємо лише дані, а не токени -> масив порожній.
            tokenAmounts: new Client.EVMTokenAmount[](0),
        // Для версії пакета 1.5+ тут може бути EVMExtraArgsV2 (allowOutOfOrderExecution).
            extraArgs: Client._argsToBytes(Client.EVMExtraArgsV1({gasLimit: gasLimit})),
            feeToken: address(linkToken)
        });
    }
}