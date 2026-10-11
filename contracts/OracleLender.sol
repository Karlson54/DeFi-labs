// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import "@openzeppelin/contracts/token/ERC20/IERC20.sol";
import "@openzeppelin/contracts/token/ERC20/utils/SafeERC20.sol";
import "@openzeppelin/contracts/utils/ReentrancyGuard.sol";
import "./interfaces/IPriceOracle.sol";

/// @title OracleLender
/// @notice Спрощений кредитний протокол (за мотивами лаб. №5): токен A як застава,
///         токен B як позика, коефіцієнт забезпечення CR = 150%.
/// @dev Джерело ціни підставляється через конструктор (Dependency Injection, як у лаб. №4):
///      із SpotPriceOracle протокол вразливий до Flash Loan-атаки, із TrustedPriceOracle — ні.
///      Ліквідність (токен B) надходить прямим ERC-20 переказом на адресу контракту.
contract OracleLender is ReentrancyGuard {
    using SafeERC20 for IERC20;

    IERC20 public immutable collateralToken;
    IERC20 public immutable borrowToken;
    IPriceOracle public immutable oracle;

    uint256 public constant COLLATERALIZATION_RATIO = 150;
    uint256 public constant RATIO_DENOMINATOR = 100;
    uint256 public constant PRECISION = 1e18;

    mapping(address => uint256) public collateral; // застава (токен A)
    mapping(address => uint256) public debt;       // борг (токен B)

    event CollateralDeposited(address indexed user, uint256 amount);
    event Borrowed(address indexed user, uint256 amount);
    event Repaid(address indexed user, uint256 amount);

    constructor(address collateralToken_, address borrowToken_, address oracle_) {
        require(
            collateralToken_ != address(0) && borrowToken_ != address(0) && oracle_ != address(0),
            "OracleLender: zero address"
        );
        require(collateralToken_ != borrowToken_, "OracleLender: identical tokens");
        collateralToken = IERC20(collateralToken_);
        borrowToken = IERC20(borrowToken_);
        oracle = IPriceOracle(oracle_);
    }

    /// @dev Checks-Effects-Interactions: спершу стан, потім transferFrom.
    function depositCollateral(uint256 amount) external nonReentrant {
        require(amount > 0, "OracleLender: amount must be > 0");
        collateral[msg.sender] += amount;
        collateralToken.safeTransferFrom(msg.sender, address(this), amount);
        emit CollateralDeposited(msg.sender, amount);
    }

    /// @notice Скільки токенів B користувач ще може позичити за ПОТОЧНОЮ ціною оракула.
    function maxBorrowable(address user) public view returns (uint256) {
        uint256 collateralValue = (collateral[user] * oracle.getPrice()) / PRECISION;
        uint256 maxDebt = (collateralValue * RATIO_DENOMINATOR) / COLLATERALIZATION_RATIO;
        uint256 currentDebt = debt[user];
        return maxDebt > currentDebt ? maxDebt - currentDebt : 0;
    }

    function borrow(uint256 amount) external nonReentrant {
        require(amount > 0, "OracleLender: amount must be > 0");
        require(amount <= maxBorrowable(msg.sender), "OracleLender: exceeds borrow limit");
        debt[msg.sender] += amount;
        borrowToken.safeTransfer(msg.sender, amount);
        emit Borrowed(msg.sender, amount);
    }

    function repay(uint256 amount) external nonReentrant {
        require(amount > 0 && amount <= debt[msg.sender], "OracleLender: bad repay amount");
        debt[msg.sender] -= amount;
        borrowToken.safeTransferFrom(msg.sender, address(this), amount);
        emit Repaid(msg.sender, amount);
    }
}