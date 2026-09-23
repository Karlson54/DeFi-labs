using DeFi.Data;
using DeFi.Models;
using DeFi.Models.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;

namespace DeFi.Services;

public sealed class SwapIndexerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWeb3Factory _web3Factory;
    private readonly IDeploymentStateStore _stateStore;
    private readonly IndexerSettings _settings;
    private readonly ILogger<SwapIndexerService> _logger;

    public SwapIndexerService(
        IServiceScopeFactory scopeFactory,
        IWeb3Factory web3Factory,
        IDeploymentStateStore stateStore,
        IOptions<IndexerSettings> settings,
        ILogger<SwapIndexerService> logger)
    {
        _scopeFactory = scopeFactory;
        _web3Factory = web3Factory;
        _stateStore = stateStore;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(Math.Max(1, _settings.PollingIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Індексатор: помилка опитування, повтор через {Seconds}с.", delay.TotalSeconds);
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        var deployer = _web3Factory.AccountAddress;
        var chainId = _web3Factory.ChainId;

        var state = await _stateStore.LoadAsync(chainId, deployer, cancellationToken);

        if (string.IsNullOrWhiteSpace(state.PoolAddress))
        {
            _logger.LogInformation("Індексатор: пул ще не розгорнутий (deployment-state.json порожній) — очікую.");
            return;
        }

        var poolAddress = state.PoolAddress!;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SwapIndexerDbContext>();
        await db.Database.EnsureCreatedAsync(cancellationToken);

        var checkpoint = await db.Checkpoints.FirstOrDefaultAsync(
            c => c.ChainId == chainId && c.PoolAddress == poolAddress, cancellationToken);

        var fromBlock = checkpoint is null ? _settings.StartBlock : checkpoint.LastProcessedBlock + 1;

        var latestBlockHex = await _web3Factory.Client.Eth.Blocks.GetBlockNumber.SendRequestAsync();
        var latestBlock = (long)latestBlockHex.Value;

        if (fromBlock > latestBlock)
        {
            return;
        }

        var toBlock = Math.Min(latestBlock, fromBlock + _settings.MaxBlockRangePerPoll - 1);

        var eventHandler = _web3Factory.Client.Eth.GetEvent<SwapEventDto>(poolAddress);
        var filter = eventHandler.CreateFilterInput(
            new BlockParameter((ulong)fromBlock),
            new BlockParameter((ulong)toBlock));

        var logs = await eventHandler.GetAllChangesAsync(filter);

        foreach (var log in logs)
        {
            var logIndex = (int)log.Log.LogIndex.Value;

            var exists = await db.SwapRecords.AnyAsync(
                r => r.TransactionHash == log.Log.TransactionHash && r.LogIndex == logIndex,
                cancellationToken);

            if (exists)
            {
                continue;
            }

            db.SwapRecords.Add(new SwapRecordEntity
            {
                TransactionHash = log.Log.TransactionHash,
                LogIndex = logIndex,
                BlockNumber = (long)log.Log.BlockNumber.Value,
                PoolAddress = poolAddress,
                Trader = log.Event.Trader,
                TokenIn = log.Event.TokenIn,
                AmountIn = Web3.Convert.FromWei(log.Event.AmountIn),
                AmountOut = Web3.Convert.FromWei(log.Event.AmountOut),
                FeeBps = (int)log.Event.FeeBps
            });
        }

        if (checkpoint is null)
        {
            db.Checkpoints.Add(new IndexerCheckpointEntity
            {
                ChainId = chainId,
                PoolAddress = poolAddress,
                LastProcessedBlock = toBlock
            });
        }
        else
        {
            checkpoint.LastProcessedBlock = toBlock;
        }

        await db.SaveChangesAsync(cancellationToken);

        if (logs.Count > 0)
        {
            _logger.LogInformation(
                "Індексатор: збережено {Count} нових Swap-подій (блоки {From}-{To}).",
                logs.Count, fromBlock, toBlock);
        }
    }
}