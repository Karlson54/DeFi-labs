// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

// Успадковуємо проаудійовану реалізацію ERC-20 від OpenZeppelin:
// у DeFi не прийнято писати облік балансів з нуля, бо будь-яка помилка = втрата коштів.
import "@openzeppelin/contracts/token/ERC20/ERC20.sol";

/// @title AssetToken
/// @notice Універсальний синтетичний актив (токенізована гривня, "акція", мок-фіат тощо).
/// @dev Уся математика балансів, allowance та подій Transfer/Approval успадкована з ERC20.
contract AssetToken is ERC20 {
    /// @param name_ повна назва активу (наприклад, "Ruban Andrii Coin")
    /// @param symbol_ тікер активу (наприклад, "RUBC")
    /// @param initialSupply_ початкова емісія у мінімальних одиницях (wei, тобто з 18 десятковими)
    constructor(string memory name_, string memory symbol_, uint256 initialSupply_)
        ERC20(name_, symbol_)
    {
        // Вся емісія одразу зараховується розгортачу контракту — він же виступає
        // провайдером ліквідності у симуляції.
        _mint(msg.sender, initialSupply_);
    }
}
