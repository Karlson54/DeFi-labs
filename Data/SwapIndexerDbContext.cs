using DeFi.Models;
using Microsoft.EntityFrameworkCore;

namespace DeFi.Data;

public sealed class SwapIndexerDbContext : DbContext
{
    public SwapIndexerDbContext(DbContextOptions<SwapIndexerDbContext> options) : base(options)
    {
    }

    public DbSet<SwapRecordEntity> SwapRecords => Set<SwapRecordEntity>();
    public DbSet<IndexerCheckpointEntity> Checkpoints => Set<IndexerCheckpointEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SwapRecordEntity>(entity =>
        {
            entity.HasIndex(x => x.Trader);
            entity.HasIndex(x => new { x.TransactionHash, x.LogIndex }).IsUnique();
            entity.Property(x => x.AmountIn).HasColumnType("decimal(38,18)");
            entity.Property(x => x.AmountOut).HasColumnType("decimal(38,18)");
        });

        modelBuilder.Entity<IndexerCheckpointEntity>(entity =>
        {
            entity.HasKey(x => new { x.ChainId, x.PoolAddress });
        });
    }
}