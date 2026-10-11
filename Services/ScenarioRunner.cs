using System.Numerics;
using DeFi.Models;
using Microsoft.Extensions.Options;
using Nethereum.Web3;

namespace DeFi.Services;

public interface IScenarioRunner
{
    Task<ScenarioReport> RunAsync(CancellationToken cancellationToken = default);
}

public sealed class ScenarioRunner : IScenarioRunner
{
    private const int Decimals = 18;

    private readonly IWeb3Factory _web3Factory;
    private readonly ITokenService _tokenService;
    private readonly IProtocolService _protocolService;
    private readonly IDaoService _daoService;
    private readonly IDeploymentStateStore _stateStore;
    private readonly Lab9Settings _settings;

    public ScenarioRunner(
        IWeb3Factory web3Factory,
        ITokenService tokenService,
        IProtocolService protocolService,
        IDaoService daoService,
        IDeploymentStateStore stateStore,
        IOptions<Lab9Settings> settingsOptions)
    {
        _web3Factory = web3Factory;
        _tokenService = tokenService;
        _protocolService = protocolService;
        _daoService = daoService;
        _stateStore = stateStore;
        _settings = settingsOptions.Value;
    }

    public async Task<ScenarioReport> RunAsync(CancellationToken cancellationToken = default)
    {
        ValidateSettings();

        var user = _web3Factory.AccountAddress;
        var chainId = _web3Factory.ChainId;
        var newFee = new BigInteger(_settings.Dao.NewFee);

        var state = await _stateStore.LoadAsync(chainId, user, cancellationToken);
        var session = new DeploymentSession(_web3Factory, _stateStore, state);

        var token = await session.EnsureAsync(StateKeys.Token, _settings.TokenA.Name, async () =>
        {
            var deployed = await _tokenService.DeployAsync(_settings.TokenA, cancellationToken);
            return new DeployedContract(deployed.Address, deployed.TransactionHash!);
        }, cancellationToken);

        var protocol = await session.EnsureAsync(StateKeys.Protocol, "MockProtocol",
            () => _protocolService.DeployAsync(new BigInteger(_settings.Dao.InitialFee), cancellationToken),
            cancellationToken);

        var dao = await session.EnsureAsync(StateKeys.Dao, "SimpleDAO",
            () => _daoService.DeployAsync(token.Address, cancellationToken),
            cancellationToken);

        var ownership = await _protocolService.TransferOwnershipAsync(protocol.Address, dao.Address, cancellationToken);

        var direct = await _protocolService.TrySetFeeDirectlyAsync(protocol.Address, newFee, cancellationToken);
        if (!direct.Reverted)
        {
            throw new InvalidOperationException(
                "КРИТИЧНО: прямий виклик setFee від колишнього власника пройшов успішно. " +
                "Перевірте, що transferOwnership передав права саме контракту SimpleDAO.");
        }

        var feeBefore = await _protocolService.GetFeeAsync(protocol.Address, cancellationToken);

        var payload = await _protocolService.EncodeSetFeeAsync(protocol.Address, newFee, cancellationToken);
        var proposal = await _daoService.CreateProposalAsync(
            dao.Address, protocol.Address, payload, _settings.Dao.ProposalDescription, cancellationToken);

        var weightWei = await _tokenService.BalanceOfAsync(token.Address, user, cancellationToken);
        var voteTx = await _daoService.VoteAsync(dao.Address, proposal.ProposalId, true, cancellationToken);
        var vote = new VoteResult(Web3.Convert.FromWei(weightWei, Decimals), true, voteTx.TransactionHash, voteTx.GasUsed);
        var afterVote = await _daoService.GetProposalAsync(dao.Address, proposal.ProposalId, cancellationToken);

        var waited = await WaitForDeadlineAsync(proposal.Deadline, cancellationToken);
        var execute = await _daoService.ExecuteProposalAsync(dao.Address, proposal.ProposalId, cancellationToken);
        var afterExecute = await _daoService.GetProposalAsync(dao.Address, proposal.ProposalId, cancellationToken);

        var feeAfter = await _protocolService.GetFeeAsync(protocol.Address, cancellationToken);
        if (feeAfter != newFee)
        {
            throw new InvalidOperationException(
                $"КРИТИЧНО: після executeProposal комісія = {feeAfter}, очікувалось {newFee}. " +
                "Перевірте payload пропозиції та права власності DAO на MockProtocol.");
        }

        return new ScenarioReport(
            Network: $"chainId {chainId}",
            UserAddress: user,
            TokenSymbol: _settings.TokenA.Symbol,
            Token: token,
            Protocol: protocol,
            Dao: dao,
            Ownership: ownership,
            DirectAttempt: direct,
            FeeBefore: feeBefore,
            NewFee: newFee,
            Proposal: proposal,
            Vote: vote,
            AfterVote: afterVote,
            Waited: waited,
            Execute: execute,
            AfterExecute: afterExecute,
            FeeAfter: feeAfter);
    }

    private async Task<TimeSpan> WaitForDeadlineAsync(DateTimeOffset deadline, CancellationToken cancellationToken)
    {
        var target = deadline.AddSeconds(Math.Max(0, _settings.Dao.VotingWaitBufferSeconds));
        var started = DateTimeOffset.UtcNow;

        Log($"Очікування завершення голосування до {deadline.LocalDateTime:HH:mm:ss}...");

        while (DateTimeOffset.UtcNow < target)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var left = target - DateTimeOffset.UtcNow;
            Log($"Залишилось ≈ {Math.Ceiling(left.TotalSeconds)} с");
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(10, Math.Max(1, left.TotalSeconds))), cancellationToken);
        }

        return DateTimeOffset.UtcNow - started;
    }

    private void ValidateSettings()
    {
        var dao = _settings.Dao;

        if (dao.NewFee <= 0 || dao.NewFee > 100 || dao.InitialFee < 0 || dao.InitialFee > 100)
        {
            throw new InvalidOperationException(
                "У Lab9Settings:Dao комісії мають бути в діапазоні 0..100 (NewFee > 0): у MockProtocol MAX_FEE = 100.");
        }

        if (string.IsNullOrWhiteSpace(_settings.TokenA.Symbol) || string.IsNullOrWhiteSpace(_settings.TokenA.Name))
        {
            throw new InvalidOperationException("У Lab9Settings:TokenA мають бути задані Name та Symbol.");
        }
    }

    private static void Log(string message) =>
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
}