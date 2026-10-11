// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import "@openzeppelin/contracts/token/ERC20/IERC20.sol";
import "@openzeppelin/contracts/utils/ReentrancyGuard.sol";

/// @title SimpleDAO
/// @notice Мінімальна децентралізована автономна організація: пропозиції, голосування
///         governance-токеном та автоматичне виконання через низькорівневий call.
/// @dev Вага голосу = баланс governance-токена на момент голосування.
///      НАВЧАЛЬНЕ СПРОЩЕННЯ: балансі без snapshot вразливі до подвійного голосування
///      (переказ токенів на іншу адресу) та до flash-loan governance. Див. README, п. 6.
contract SimpleDAO is ReentrancyGuard {
    IERC20 public immutable governanceToken;

    // Для тестування 1 хвилина. У продакшені тут було б 3 days.
    uint256 public constant VOTING_PERIOD = 1 minutes;

    // Кворум: частка від totalSupply, що мусить проголосувати (у відсотках).
    uint256 public constant QUORUM_PERCENT = 10;

    struct Proposal {
        address target;        // цільовий контракт
        bytes payload;         // закодований виклик функції (selector + аргументи)
        string description;
        uint256 deadline;      // кінець голосування (unix-час)
        uint256 votesFor;
        uint256 votesAgainst;
        bool executed;
    }

    uint256 public proposalCount;
    mapping(uint256 => Proposal) public proposals;
    mapping(uint256 => mapping(address => bool)) public hasVoted;

    event ProposalCreated(
        uint256 indexed id, address indexed proposer, address target, uint256 deadline, string description
    );
    event Voted(uint256 indexed id, address indexed voter, bool support, uint256 weight);
    event ProposalExecuted(uint256 indexed id, address indexed executor);

    constructor(address governanceToken_) {
        require(governanceToken_ != address(0), "SimpleDAO: zero token");
        governanceToken = IERC20(governanceToken_);
    }

    /// @notice Створення пропозиції. Ідентифікатори починаються з 1.
    function createProposal(
        address _targetContract,
        bytes calldata _payload,
        string calldata _description
    ) external returns (uint256 proposalId) {
        require(_targetContract != address(0), "SimpleDAO: zero target");
        require(_payload.length >= 4, "SimpleDAO: empty payload");
        require(governanceToken.balanceOf(msg.sender) > 0, "SimpleDAO: no governance tokens");

        proposalId = ++proposalCount;

        Proposal storage p = proposals[proposalId];
        p.target = _targetContract;
        p.payload = _payload;
        p.description = _description;
        p.deadline = block.timestamp + VOTING_PERIOD;

        emit ProposalCreated(proposalId, msg.sender, _targetContract, p.deadline, _description);
    }

    /// @notice Голосування: вага голосу пропорційна балансу governance-токена.
    function vote(uint256 proposalId, bool support) external {
        Proposal storage p = proposals[proposalId];
        require(p.target != address(0), "SimpleDAO: unknown proposal");
        require(block.timestamp < p.deadline, "SimpleDAO: voting closed");
        require(!hasVoted[proposalId][msg.sender], "SimpleDAO: already voted");

        uint256 weight = governanceToken.balanceOf(msg.sender);
        require(weight > 0, "SimpleDAO: no voting power");

        hasVoted[proposalId][msg.sender] = true;
        if (support) {
            p.votesFor += weight;
        } else {
            p.votesAgainst += weight;
        }

        emit Voted(proposalId, msg.sender, support, weight);
    }

    /// @notice Виконання пропозиції після завершення голосування.
    /// @dev Checks-Effects-Interactions: executed = true ДО зовнішнього виклику, плюс
    ///      nonReentrant. Для цілі DAO є msg.sender, тобто owner (адміністратор).
    function executeProposal(uint256 proposalId) external nonReentrant {
        Proposal storage p = proposals[proposalId];
        require(p.target != address(0), "SimpleDAO: unknown proposal");
        require(block.timestamp >= p.deadline, "SimpleDAO: voting not finished");
        require(!p.executed, "SimpleDAO: already executed");
        require(p.votesFor > p.votesAgainst, "SimpleDAO: proposal rejected");
        require(
            (p.votesFor + p.votesAgainst) * 100 >= governanceToken.totalSupply() * QUORUM_PERCENT,
            "SimpleDAO: quorum not reached"
        );

        p.executed = true;

        // Найпотужніший (і найнебезпечніший) механізм EVM: довільний низькорівневий виклик.
        (bool ok, ) = p.target.call(p.payload);
        require(ok, "SimpleDAO: call failed");

        emit ProposalExecuted(proposalId, msg.sender);
    }
}