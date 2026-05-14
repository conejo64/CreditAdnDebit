using IsoSwitch.Infrastructure.Persistence.Routing;
using IsoSwitch.Infrastructure.Persistence.Catalog;
using IsoSwitch.Infrastructure.Persistence.IsoAudit;
using IsoSwitch.Infrastructure.Persistence.Audit;
using IsoSwitch.Infrastructure.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;

namespace IsoSwitch.Infrastructure.Persistence;

public sealed class IsoSwitchDbContext : DbContext
{
    public IsoSwitchDbContext(DbContextOptions<IsoSwitchDbContext> options) : base(options) { }

    public DbSet<RoutingRuleCacheEntity> RoutingRulesCache => Set<RoutingRuleCacheEntity>();
    public DbSet<IsoSwitch.Infrastructure.Persistence.Routing.RoutingRuleV2Entity> RoutingRulesV2 => Set<IsoSwitch.Infrastructure.Persistence.Routing.RoutingRuleV2Entity>();
    public DbSet<TransactionEntity> Transactions => Set<TransactionEntity>();

    public DbSet<CountryCacheEntity> CountriesCache => Set<CountryCacheEntity>();
    public DbSet<BinRangeCacheEntity> BinRangesCache => Set<BinRangeCacheEntity>();
    public DbSet<CardProductCacheEntity> CardProductsCache => Set<CardProductCacheEntity>();
    public DbSet<CurrencyCacheEntity> CurrenciesCache => Set<CurrencyCacheEntity>();
    public DbSet<NetworkCacheEntity> NetworksCache => Set<NetworkCacheEntity>();
    public DbSet<ParticipantCacheEntity> ParticipantsCache => Set<ParticipantCacheEntity>();

    public DbSet<IsoMessageLogEntity> IsoMessageLogs => Set<IsoMessageLogEntity>();
    public DbSet<AuditEventEntity> AuditEvents => Set<AuditEventEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RoutingRuleCacheEntity>(b =>
        {
            b.ToTable("RoutingRulesCache");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Priority);
            b.HasIndex(x => new { x.BinStart, x.BinEnd });
        });



        modelBuilder.Entity<IsoSwitch.Infrastructure.Persistence.Routing.RoutingRuleV2Entity>(b =>
        {
            b.ToTable("RoutingRulesV2");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Priority);
            b.HasIndex(x => new { x.BinStart, x.BinEnd });
            b.HasIndex(x => x.CountryCode);
            b.HasIndex(x => x.Network);
            b.HasIndex(x => x.TxType);
        });

        

        modelBuilder.Entity<AuditEventEntity>(b =>
        {
            b.ToTable("AuditEvents");
            b.HasKey(x => x.Id);
            b.Property(x => x.Service).HasMaxLength(64);
            b.Property(x => x.EventType).HasMaxLength(128).IsRequired();
            b.Property(x => x.CorrelationId).HasMaxLength(64);
            b.Property(x => x.TraceId).HasMaxLength(64);
            b.Property(x => x.PayloadSha256).HasMaxLength(64);
            b.HasIndex(x => x.OccurredOn);
            b.HasIndex(x => x.EventType);
        });

modelBuilder.Entity<TransactionEntity>(b =>
        {
            b.ToTable("Transactions");
            b.HasKey(x => x.Id);
            b.Property(x => x.TraceId).IsRequired();
            b.Property(x => x.Stan).IsRequired();
            b.Property(x => x.ConnectorId).IsRequired();
            b.Property(x => x.RequestJson).IsRequired();
            b.Property(x => x.Status).IsRequired();
            b.Property(x => x.InDoubt);
            b.Property(x => x.ReversalStatus);
            b.HasIndex(x => x.TraceId).IsUnique();
        });
        modelBuilder.Entity<IsoMessageLogEntity>(b =>
        {
            b.ToTable("iso_message_logs");
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.TraceId, x.Direction }).IsUnique();
            b.HasIndex(x => x.Mti);
            b.HasIndex(x => x.CreatedOn);
        });
    }
}