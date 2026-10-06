// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import "@openzeppelin/contracts/token/ERC20/ERC20.sol";
import "@openzeppelin/contracts/access/Ownable.sol";

/// @title StableCoin
/// @notice Алгоритмічний стейблкоїн (синтетичний актив, прив'язаний до USD).
/// @dev Випускати (mint) та спалювати (burn) токени може лише власник —
///      після розгортання власність передається контракту StableEngine
///      (transferOwnership), тому жодна людина не може емітувати токени "з повітря".
contract StableCoin is ERC20, Ownable {
    /// @param name_ повна назва активу (наприклад, "RubanUSD")
    /// @param symbol_ тікер активу (наприклад, "RUBUSD")
    constructor(string memory name_, string memory symbol_)
    ERC20(name_, symbol_)
    Ownable(msg.sender) // початковий власник — розгортач; далі він передає права рушію
    {}

    /// @notice Емісія нових токенів. Доступна лише власнику (StableEngine).
    function mint(address to, uint256 amount) external onlyOwner {
        _mint(to, amount);
    }

    /// @notice Спалювання токенів з балансу `from`. Доступне лише власнику (StableEngine).
    /// @dev Рушій спалює лише ті токени, які користувач сам йому повернув (transferFrom),
    ///      тому власник не може "відібрати" чужі кошти.
    function burn(address from, uint256 amount) external onlyOwner {
        _burn(from, amount);
    }
}