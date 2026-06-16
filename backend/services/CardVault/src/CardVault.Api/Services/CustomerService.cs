using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Issuer;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Api.Services;

public sealed class CustomerService
{
    private readonly CardVaultDbContext _db;
    public CustomerService(CardVaultDbContext db) => _db = db;

    public async Task<CustomerEntity> CreateAsync(string fullName, string documentId, string email, string phone, string documentType, string gender, string billingAddress, string stmtAddress, string resCity, string stmtCity, string cardCity, CancellationToken ct)
    {
        var customerNumber = "C" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var c = new CustomerEntity
        {
            Id = Guid.NewGuid(),
            CustomerNumber = customerNumber,
            FullName = fullName,
            DocumentId = documentId,
            Email = email,
            Phone = phone,
            DocumentType = documentType ?? "CEDULA",
            Gender = gender ?? "N/A",
            BillingAddress = billingAddress ?? "N/A",
            StatementAddress = stmtAddress ?? "N/A",
            ResidenceCity = resCity ?? "N/A",
            StatementCity = stmtCity ?? "N/A",
            CardDeliveryCity = cardCity ?? "N/A",
            CreatedOn = DateTimeOffset.UtcNow
        };
        _db.Customers.Add(c);
        await _db.SaveChangesAsync(ct);
        return c;
    }

    public Task<CustomerEntity?> GetAsync(Guid id, CancellationToken ct) =>
        _db.Customers.Include(x => x.Accounts).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<List<CustomerEntity>> SearchAsync(string? q, int take, CancellationToken ct)
    {
        var query = _db.Customers.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(x => x.FullName.ToLower().Contains(q.ToLower()) || x.DocumentId.Contains(q));
        return query.OrderByDescending(x => x.CreatedOn).Take(take).ToListAsync(ct);
    }
}
