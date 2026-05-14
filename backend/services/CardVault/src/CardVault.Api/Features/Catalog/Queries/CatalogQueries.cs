using MediatR;
using Microsoft.EntityFrameworkCore;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Catalog;

namespace CardVault.Api.Features.Catalog.Queries;

// Queries
public record GetCountriesQuery() : IRequest<List<CountryEntity>>;
public record GetBinRangesQuery() : IRequest<List<BinRangeEntity>>;
public record GetCardProductsQuery() : IRequest<List<CardProductEntity>>;

// Handlers
public class GetCountriesQueryHandler : IRequestHandler<GetCountriesQuery, List<CountryEntity>>
{
    private readonly CardVaultDbContext _db;
    public GetCountriesQueryHandler(CardVaultDbContext db) => _db = db;

    public Task<List<CountryEntity>> Handle(GetCountriesQuery request, CancellationToken ct)
        => _db.Countries.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
}

public class GetBinRangesQueryHandler : IRequestHandler<GetBinRangesQuery, List<BinRangeEntity>>
{
    private readonly CardVaultDbContext _db;
    public GetBinRangesQueryHandler(CardVaultDbContext db) => _db = db;

    public Task<List<BinRangeEntity>> Handle(GetBinRangesQuery request, CancellationToken ct)
        => _db.BinRanges.AsNoTracking().OrderBy(x => x.BinStart).ToListAsync(ct);
}

public class GetCardProductsQueryHandler : IRequestHandler<GetCardProductsQuery, List<CardProductEntity>>
{
    private readonly CardVaultDbContext _db;
    public GetCardProductsQueryHandler(CardVaultDbContext db) => _db = db;

    public Task<List<CardProductEntity>> Handle(GetCardProductsQuery request, CancellationToken ct)
        => _db.CardProducts.AsNoTracking().OrderBy(x => x.Brand).ThenBy(x => x.Name).ToListAsync(ct);
}
