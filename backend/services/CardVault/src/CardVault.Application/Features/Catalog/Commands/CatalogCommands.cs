using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Catalog;
using CardVault.Infrastructure.Persistence.Outbox;

namespace CardVault.Application.Features.Catalog.Commands;

public record CreateCountryRequestDto(string Code, string Name, string NumericCode, string Currency);
public record CreateBinRangeRequestDto(int BinStart, int BinEnd, string Brand, string Product, string? IssuerName, string? CountryCode);
public record CreateCardProductRequestDto(string Code, string Brand, string ProductType, string Name);

public record CreateCountryCommand(CreateCountryRequestDto Payload) : IRequest<IResult>;
public record CreateBinRangeCommand(CreateBinRangeRequestDto Payload) : IRequest<IResult>;
public record CreateCardProductCommand(CreateCardProductRequestDto Payload) : IRequest<IResult>;

public class CreateCountryCommandHandler : IRequestHandler<CreateCountryCommand, IResult>
{
    private readonly CardVaultDbContext _db;
    public CreateCountryCommandHandler(CardVaultDbContext db) => _db = db;

    public async Task<IResult> Handle(CreateCountryCommand request, CancellationToken ct)
    {
        var req = request.Payload;
        var code = req.Code.Trim().ToUpperInvariant();
        if (code.Length != 2) return Results.BadRequest(new { message = "Country code must be alpha-2" });
        
        var exists = await _db.Countries.FindAsync(new object[] { code }, ct);
        if (exists is not null) return Results.Conflict(new { message = "Country already exists" });

        var entity = new CountryEntity
        {
            Code = code, Name = req.Name.Trim(), NumericCode = req.NumericCode.Trim(), Currency = req.Currency.Trim().ToUpperInvariant(), Enabled = true
        };
        _db.Countries.Add(entity);

        var payload = System.Text.Json.JsonSerializer.Serialize(new { type = "cv.catalog.country.upserted", code, name = entity.Name, numericCode = entity.NumericCode, currency = entity.Currency, enabled = true, updatedOn = DateTimeOffset.UtcNow });
        _db.OutboxMessages.Add(new OutboxMessageEntity { Topic = "cv.catalog.country.upserted", Key = code, PayloadJson = payload });
        
        await _db.SaveChangesAsync(ct);
        return Results.Created($"/api/catalog/countries/{code}", new { code });
    }
}

public class CreateBinRangeCommandHandler : IRequestHandler<CreateBinRangeCommand, IResult>
{
    private readonly CardVaultDbContext _db;
    public CreateBinRangeCommandHandler(CardVaultDbContext db) => _db = db;

    public async Task<IResult> Handle(CreateBinRangeCommand request, CancellationToken ct)
    {
        var req = request.Payload;
        if (req.BinStart <= 0 || req.BinEnd <= 0 || req.BinEnd < req.BinStart)
            return Results.BadRequest(new { message = "Invalid bin range" });

        var entity = new BinRangeEntity
        {
            BinStart = req.BinStart, BinEnd = req.BinEnd, Brand = req.Brand.Trim().ToUpperInvariant(),
            Product = req.Product.Trim().ToUpperInvariant(), IssuerName = req.IssuerName?.Trim(),
            CountryCode = req.CountryCode?.Trim().ToUpperInvariant(), Enabled = true, UpdatedOn = DateTimeOffset.UtcNow
        };
        _db.BinRanges.Add(entity);

        var payload = System.Text.Json.JsonSerializer.Serialize(new { type = "cv.catalog.binrange.upserted", id = entity.Id, binStart = entity.BinStart, binEnd = entity.BinEnd, brand = entity.Brand, product = entity.Product, issuerName = entity.IssuerName, countryCode = entity.CountryCode, enabled = entity.Enabled, updatedOn = entity.UpdatedOn });
        _db.OutboxMessages.Add(new OutboxMessageEntity { Topic = "cv.catalog.binrange.upserted", Key = entity.Id.ToString("N"), PayloadJson = payload });
        
        await _db.SaveChangesAsync(ct);
        return Results.Created($"/api/catalog/bins/{entity.Id}", entity);
    }
}

public class CreateCardProductCommandHandler : IRequestHandler<CreateCardProductCommand, IResult>
{
    private readonly CardVaultDbContext _db;
    public CreateCardProductCommandHandler(CardVaultDbContext db) => _db = db;

    public async Task<IResult> Handle(CreateCardProductCommand request, CancellationToken ct)
    {
        var req = request.Payload;
        var code = req.Code.Trim().ToUpperInvariant();
        var exists = await _db.CardProducts.FirstOrDefaultAsync(x => x.Code == code, ct);
        if (exists is not null) return Results.Conflict(new { message = "Card product already exists" });

        var entity = new CardProductEntity
        {
            Code = code, Brand = req.Brand.Trim().ToUpperInvariant(), ProductType = req.ProductType.Trim().ToUpperInvariant(),
            Name = req.Name.Trim(), Enabled = true, UpdatedOn = DateTimeOffset.UtcNow
        };
        _db.CardProducts.Add(entity);

        var payload = System.Text.Json.JsonSerializer.Serialize(new { type = "cv.catalog.cardproduct.upserted", id = entity.Id, code = entity.Code, brand = entity.Brand, productType = entity.ProductType, name = entity.Name, enabled = entity.Enabled, updatedOn = entity.UpdatedOn });
        _db.OutboxMessages.Add(new OutboxMessageEntity { Topic = "cv.catalog.cardproduct.upserted", Key = entity.Code, PayloadJson = payload });
        
        await _db.SaveChangesAsync(ct);
        return Results.Created($"/api/catalog/card-products/{entity.Id}", entity);
    }
}
