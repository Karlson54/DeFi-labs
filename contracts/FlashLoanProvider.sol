// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import "@openzeppelin/contracts/token/ERC20/IERC20.sol";
import "@openzeppelin/contracts/token/ERC20/utils/SafeERC20.sol";
import "@openzeppelin/contracts/utils/ReentrancyGuard.sol";
import "./interfaces/IFlashLoan.sol";

/// @title FlashLoanProvider
/// @notice Кредитор миттєвих позик: видає токени без застави, але вимагає повернення
///         суми разом із комісією 0.09% в межах ТІЄЇ САМОЇ транзакції.
/// @dev Патерн Callback: переказ -> виклик executeOperation у позичальника -> перевірка
///      балансу. Якщо баланс не відновлено, require відкочує всю транзакцію (EVM Revert),
///      тому ризик кредитора дорівнює нулю. Ліквідність надходить прямим переказом.
contract FlashLoanProvider is ReentrancyGuard {
    using SafeERC20 for IERC20;

    IERC20 public immutable token;

    uint256 public constant FEE_BPS = 9;              // 0.09%
    uint256 public constant FEE_DENOMINATOR = 10_000;

    event FlashLoan(address indexed receiver, uint256 amount, uint256 fee);

    constructor(address token_) {
        require(token_ != address(0), "FlashLoanProvider: zero token");
        token = IERC20(token_);
    }

    /// @notice Комісія округлюється ВГОРУ — на користь кредитора.
    function flashFee(uint256 amount) public pure returns (uint256) {
        return (amount * FEE_BPS + FEE_DENOMINATOR - 1) / FEE_DENOMINATOR;
    }

    function flashLoan(address receiver, uint256 amount, bytes calldata params) external nonReentrant {
        require(receiver != address(0), "FlashLoanProvider: zero receiver");
        require(amount > 0, "FlashLoanProvider: zero amount");

        uint256 balanceBefore = token.balanceOf(address(this));
        require(amount <= balanceBefore, "FlashLoanProvider: insufficient liquidity");

        uint256 fee = flashFee(amount);

        token.safeTransfer(receiver, amount);
        IFlashBorrower(receiver).executeOperation(address(token), amount, fee, msg.sender, params);

        require(
            token.balanceOf(address(this)) >= balanceBefore + fee,
            "FlashLoanProvider: loan not repaid"
        );

        emit FlashLoan(receiver, amount, fee);
    }
}