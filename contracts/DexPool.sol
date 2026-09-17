// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import "@openzeppelin/contracts/token/ERC20/IERC20.sol";

/// @title DexPool
/// @notice Пул ліквідності за моделлю константного добутку (x * y = k)
///         з ДИНАМІЧНОЮ комісією (контрольне завдання, п. 2.6).
/// @dev Комісія залежить від обсягу угоди відносно резерву вхідного токена:
///      чим "жирніша" угода — тим більший відсоток забирає пул на користь LP.
contract DexPool {
    // Адреси пари фіксуються при розгортанні й не змінюються -> immutable (економія газу).
    IERC20 public immutable tokenA;
    IERC20 public immutable tokenB;

    // Внутрішній облік пулу: це і є x та y з формули (2.1).
    uint256 public reserveA;
    uint256 public reserveB;

    // Працюємо в базисних пунктах (bps): 10000 bps = 100%.
    // EVM не має чисел з рухомою комою, тому весь відсоток — цілочисельні дроби.
    uint256 public constant FEE_DENOMINATOR = 10000;

    uint256 public constant FEE_SMALL = 10;    // 0.10% — угода < 1% резерву
    uint256 public constant FEE_MEDIUM = 30;   // 0.30% — угода < 5% резерву (класичний Uniswap V2)
    uint256 public constant FEE_LARGE = 100;   // 1.00% — угода < 10% резерву
    uint256 public constant FEE_WHALE = 500;   // 5.00% — "кит", угода >= 10% резерву

    event LiquidityAdded(address indexed provider, uint256 amountA, uint256 amountB);
    event Swap(
        address indexed trader,
        address indexed tokenIn,
        uint256 amountIn,
        uint256 amountOut,
        uint256 feeBps
    );

    constructor(address tokenA_, address tokenB_) {
        require(tokenA_ != address(0) && tokenB_ != address(0), "DexPool: zero address");
        require(tokenA_ != tokenB_, "DexPool: identical tokens");
        tokenA = IERC20(tokenA_);
        tokenB = IERC20(tokenB_);
    }

    /// @notice Динамічна комісія у базисних пунктах.
    /// @dev shareBps — яку частку резерву вхідного токена "з'їдає" угода.
    ///      Економічний сенс: великі угоди сильніше зсувають ціну й підвищують ризик
    ///      непостійних втрат для LP, тому вони мають платити більше.
    function getFeeBps(uint256 amountIn, uint256 reserveIn) public pure returns (uint256) {
        require(reserveIn > 0, "DexPool: empty reserve");
        uint256 shareBps = (amountIn * FEE_DENOMINATOR) / reserveIn;

        if (shareBps < 100) return FEE_SMALL;    // < 1%
        if (shareBps < 500) return FEE_MEDIUM;   // < 5%
        if (shareBps < 1000) return FEE_LARGE;   // < 10%
        return FEE_WHALE;                        // >= 10%
    }

    /// @notice Розрахунок вихідної суми за формулою (2.2), але з динамічним f.
    /// @dev amountOut = reserveOut * (amountIn * fn) / (reserveIn * 10000 + amountIn * fn),
    ///      де fn = 10000 - feeBps. При feeBps = 30 формула вироджується у класичну
    ///      997/1000 з методички.
    function getAmountOut(uint256 amountIn, uint256 reserveIn, uint256 reserveOut)
        public
        pure
        returns (uint256)
    {
        require(amountIn > 0, "DexPool: insufficient input");
        require(reserveIn > 0 && reserveOut > 0, "DexPool: insufficient liquidity");

        uint256 feeBps = getFeeBps(amountIn, reserveIn);
        uint256 amountInWithFee = amountIn * (FEE_DENOMINATOR - feeBps);

        uint256 numerator = reserveOut * amountInWithFee;
        uint256 denominator = reserveIn * FEE_DENOMINATOR + amountInWithFee;

        return numerator / denominator;
    }

    /// @notice Внесення ліквідності. Формує (або підтримує) курс пари.
    /// @dev transferFrom спрацює лише якщо користувач заздалегідь викликав approve
    ///      у контрактах самих токенів — це і є механізм allowance стандарту ERC-20.
    function addLiquidity(uint256 amountA, uint256 amountB) external {
        require(amountA > 0 && amountB > 0, "DexPool: zero liquidity");

        require(tokenA.transferFrom(msg.sender, address(this), amountA), "DexPool: transferFrom A failed");
        require(tokenB.transferFrom(msg.sender, address(this), amountB), "DexPool: transferFrom B failed");

        reserveA += amountA;
        reserveB += amountB;

        emit LiquidityAdded(msg.sender, amountA, amountB);
    }

    /// @notice Обмін токена A на токен B.
    /// @param amountIn скільки токена A віддає трейдер
    /// @param minAmountOut захист від проковзування: менше цієї суми угода відхиляється
    function swapAForB(uint256 amountIn, uint256 minAmountOut) external returns (uint256) {
        return _swap(true, amountIn, minAmountOut);
    }

    /// @notice Обмін токена B на токен A (дзеркальний напрямок).
    function swapBForA(uint256 amountIn, uint256 minAmountOut) external returns (uint256) {
        return _swap(false, amountIn, minAmountOut);
    }

    /// @dev Єдина внутрішня реалізація свопу для обох напрямків (щоб не дублювати математику).
    function _swap(bool aToB, uint256 amountIn, uint256 minAmountOut) private returns (uint256 amountOut) {
        require(amountIn > 0, "DexPool: insufficient input");

        uint256 reserveIn = aToB ? reserveA : reserveB;
        uint256 reserveOut = aToB ? reserveB : reserveA;

        uint256 feeBps = getFeeBps(amountIn, reserveIn);
        amountOut = getAmountOut(amountIn, reserveIn, reserveOut);

        require(amountOut > 0, "DexPool: zero output");
        require(amountOut < reserveOut, "DexPool: insufficient liquidity");
        require(amountOut >= minAmountOut, "DexPool: slippage too high");

        // Checks-Effects-Interactions: спершу оновлюємо стан, потім робимо зовнішні виклики.
        if (aToB) {
            reserveA += amountIn;
            reserveB -= amountOut;
        } else {
            reserveB += amountIn;
            reserveA -= amountOut;
        }

        IERC20 tIn = aToB ? tokenA : tokenB;
        IERC20 tOut = aToB ? tokenB : tokenA;

        require(tIn.transferFrom(msg.sender, address(this), amountIn), "DexPool: transferFrom failed");
        require(tOut.transfer(msg.sender, amountOut), "DexPool: transfer failed");

        emit Swap(msg.sender, address(tIn), amountIn, amountOut, feeBps);
    }
}
