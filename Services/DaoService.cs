using System.Numerics;
using DeFi.Models;
using DeFi.Models.Contracts;
using Microsoft.Extensions.Options;
using Nethereum.Contracts;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;

namespace DeFi.Services;

public interface IDaoService
{
    Task<DeployedContract> DeployAsync(string governanceTokenAddress, CancellationToken cancellationToken = default);

    Task<ProposalCreatedResult> CreateProposalAsync(
        string daoAddress, string target, byte[] payload, string description, CancellationToken cancellationToken = default);

    Task<TxResult> VoteAsync(string daoAddress, BigInteger proposalId, bool support, CancellationToken cancellationToken = default);

    Task<TxResult> ExecuteProposalAsync(string daoAddress, BigInteger proposalId, CancellationToken cancellationToken = default);

    Task<ProposalSnapshot> GetProposalAsync(string daoAddress, BigInteger proposalId, CancellationToken cancellationToken = default);
}

public sealed class DaoService : IDaoService
{
    private const int Decimals = 18;

    private readonly IWeb3Factory _web3Factory;
    private readonly IContractDeployer _deployer;
    private readonly TimeSpan _timeout;

    public DaoService(IWeb3Factory web3Factory, IContractDeployer deployer, IOptions<Web3Settings> options)
    {
        _web3Factory = web3Factory;
        _deployer = deployer;
        _timeout = TimeSpan.FromSeconds(options.Value.TransactionTimeoutSeconds);
    }

    public Task<DeployedContract> DeployAsync(string governanceTokenAddress, CancellationToken cancellationToken = default) =>
        _deployer.DeployAsync<SimpleDaoDeployment>(
            "SimpleDAO", m => m.GovernanceToken = governanceTokenAddress,
            "Перевірте адресу governance-токена та байткод артефакту SimpleDAO.json.",
            cancellationToken);

    public async Task<ProposalCreatedResult> CreateProposalAsync(
        string daoAddress, string target, byte[] payload, string description, CancellationToken cancellationToken = default)
    {
        var receipt = await SendAsync(
            daoAddress,
            new CreateProposalFunction { TargetContract = target, Payload = payload, Description = description },
            "Транзакція createProposal відхилена. Створювати пропозиції можуть лише власники governance-токенів.",
            cancellationToken);

        // Ідентифікатори в SimpleDAO починаються з 1, тож id нової пропозиції = proposalCount.
        var id = await _web3Factory.Client.Eth
            .GetContractQueryHandler<ProposalCountFunction>()
            .QueryAsync<BigInteger>(daoAddress, new ProposalCountFunction())
            .WaitAsync(_timeout, cancellationToken);

        var snapshot = await GetProposalAsync(daoAddress, id, cancellationToken);

        return new ProposalCreatedResult(
            id,
            "0x" + Convert.ToHexString(payload).ToLowerInvariant(),
            snapshot.Deadline,
            receipt.TransactionHash,
            receipt.GasUsed?.Value ?? BigInteger.Zero);
    }

    public async Task<TxResult> VoteAsync(string daoAddress, BigInteger proposalId, bool support, CancellationToken cancellationToken = default)
    {
        var receipt = await SendAsync(
            daoAddress,
            new VoteFunction { ProposalId = proposalId, Support = support },
            "Транзакція vote відхилена: голосування завершено, ви вже голосували або на балансі немає токенів.",
            cancellationToken);

        return new TxResult(receipt.TransactionHash, receipt.GasUsed?.Value ?? BigInteger.Zero);
    }

    public async Task<TxResult> ExecuteProposalAsync(string daoAddress, BigInteger proposalId, CancellationToken cancellationToken = default)
    {
        var receipt = await SendAsync(
            daoAddress,
            new ExecuteProposalFunction { ProposalId = proposalId },
            "Транзакція executeProposal відхилена: не завершено голосування, не набрано більшість/кворум, " +
            "або DAO не є власником цільового контракту (transferOwnership).",
            cancellationToken);

        return new TxResult(receipt.TransactionHash, receipt.GasUsed?.Value ?? BigInteger.Zero);
    }

    public async Task<ProposalSnapshot> GetProposalAsync(string daoAddress, BigInteger proposalId, CancellationToken cancellationToken = default)
    {
        var dto = await _web3Factory.Client.Eth
            .GetContractQueryHandler<ProposalsFunction>()
            .QueryDeserializingToObjectAsync<ProposalOutputDto>(
                new ProposalsFunction { ProposalId = proposalId }, daoAddress)
            .WaitAsync(_timeout, cancellationToken);

        return new ProposalSnapshot(
            proposalId,
            dto.Target,
            dto.Description,
            DateTimeOffset.FromUnixTimeSeconds((long)dto.Deadline),
            Web3.Convert.FromWei(dto.VotesFor, Decimals),
            Web3.Convert.FromWei(dto.VotesAgainst, Decimals),
            dto.Executed);
    }

    private async Task<TransactionReceipt> SendAsync<TFunction>(
        string address, TFunction function, string failureMessage, CancellationToken cancellationToken)
        where TFunction : FunctionMessage, new()
    {
        var receipt = await _web3Factory.Client.Eth
            .GetContractTransactionHandler<TFunction>()
            .SendRequestAndWaitForReceiptAsync(address, function)
            .WaitAsync(_timeout, cancellationToken);

        if (receipt.Status?.Value != 1)
        {
            throw new InvalidOperationException(failureMessage);
        }

        return receipt;
    }
}