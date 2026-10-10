// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import "@openzeppelin/contracts/utils/introspection/IERC165.sol";

/// @title Client
/// @notice Локальна копія бібліотеки структур Chainlink CCIP. Порядок полів збігається
///         з офіційною версією, тому ABI-кодування сумісне з реальним Router.
library Client {
    struct EVMTokenAmount {
        address token;
        uint256 amount;
    }

    /// @dev Повідомлення, яке Router доставляє в цільовій мережі.
    struct Any2EVMMessage {
        bytes32 messageId;
        uint64 sourceChainSelector;
        bytes sender;
        bytes data;
        EVMTokenAmount[] destTokenAmounts;
    }

    /// @dev Повідомлення, яке відправник передає Router-у у вихідній мережі.
    struct EVM2AnyMessage {
        bytes receiver;
        bytes data;
        EVMTokenAmount[] tokenAmounts;
        bytes extraArgs;
        address feeToken;
    }

    // bytes4(keccak256("CCIP EVMExtraArgsV1"))
    bytes4 public constant EVM_EXTRA_ARGS_V1_TAG = 0x97a657c9;

    struct EVMExtraArgsV1 {
        uint256 gasLimit;
    }

    function _argsToBytes(EVMExtraArgsV1 memory extraArgs) internal pure returns (bytes memory) {
        return abi.encodeWithSelector(EVM_EXTRA_ARGS_V1_TAG, extraArgs);
    }
}

/// @title IRouterClient
/// @notice Мінімальний інтерфейс CCIP Router (вихідна мережа).
interface IRouterClient {
    error UnsupportedDestinationChain(uint64 destChainSelector);
    error InsufficientFeeTokenAmount();
    error InvalidMsgValue();

    function isChainSupported(uint64 destChainSelector) external view returns (bool supported);

    function getFee(uint64 destinationChainSelector, Client.EVM2AnyMessage memory message)
    external
    view
    returns (uint256 fee);

    function ccipSend(uint64 destinationChainSelector, Client.EVM2AnyMessage calldata message)
    external
    payable
    returns (bytes32);
}

/// @title IAny2EVMMessageReceiver
/// @notice Інтерфейс, який Router очікує від отримувача повідомлень.
interface IAny2EVMMessageReceiver {
    function ccipReceive(Client.Any2EVMMessage calldata message) external;
}

/// @title CCIPReceiver
/// @notice Базовий контракт-отримувач: ccipReceive() може викликати лише Router.
abstract contract CCIPReceiver is IAny2EVMMessageReceiver, IERC165 {
    address internal immutable i_ccipRouter;

    error InvalidRouter(address router);

    constructor(address router) {
        require(router != address(0), "CCIPReceiver: zero router");
        i_ccipRouter = router;
    }

    /// @dev Router перевіряє через ERC-165, що отримувач підтримує потрібний інтерфейс.
    function supportsInterface(bytes4 interfaceId) public pure virtual override returns (bool) {
        return interfaceId == type(IAny2EVMMessageReceiver).interfaceId
            || interfaceId == type(IERC165).interfaceId;
    }

    function ccipReceive(Client.Any2EVMMessage calldata message) external virtual override onlyRouter {
        _ccipReceive(message);
    }

    function _ccipReceive(Client.Any2EVMMessage memory message) internal virtual;

    function getRouter() public view returns (address) {
        return i_ccipRouter;
    }

    modifier onlyRouter() {
        if (msg.sender != i_ccipRouter) revert InvalidRouter(msg.sender);
        _;
    }
}