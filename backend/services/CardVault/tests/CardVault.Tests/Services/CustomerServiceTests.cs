using CardVault.Application.Services;
using CardVault.Tests.Infrastructure;
using FluentAssertions;

namespace CardVault.Tests.Services;

public sealed class CustomerServiceTests : IDisposable
{
    private readonly CardVault.Infrastructure.Persistence.CardVaultDbContext _db;
    private readonly CustomerService _sut;

    public CustomerServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _sut = new CustomerService(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task CreateAsync_ShouldPersistCustomer_WithGeneratedNumber()
    {
        // Act
        var customer = await _sut.CreateAsync(
            "Jonathan Ortiz", "1234567890", "jortiz@test.com", "+593991234567",
            "CEDULA", "M", "Calle Principal 123", "Av. Quito 456",
            "Quito", "Guayaquil", "Quito", CancellationToken.None);

        // Assert
        customer.Should().NotBeNull();
        customer.Id.Should().NotBeEmpty();
        customer.CustomerNumber.Should().StartWith("C");
        customer.FullName.Should().Be("Jonathan Ortiz");
        customer.DocumentId.Should().Be("1234567890");
        customer.Email.Should().Be("jortiz@test.com");
        customer.DocumentType.Should().Be("CEDULA");
        customer.CreatedOn.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreateAsync_NullOptionalFields_ShouldDefaultToNA()
    {
        // Act
        var customer = await _sut.CreateAsync(
            "Test User", "9999999", "test@test.com", "123456",
            null!, null!, null!, null!, null!, null!, null!, CancellationToken.None);

        // Assert
        customer.DocumentType.Should().Be("CEDULA");
        customer.Gender.Should().Be("N/A");
        customer.BillingAddress.Should().Be("N/A");
        customer.StatementAddress.Should().Be("N/A");
        customer.ResidenceCity.Should().Be("N/A");
        customer.StatementCity.Should().Be("N/A");
        customer.CardDeliveryCity.Should().Be("N/A");
    }

    [Fact]
    public async Task GetAsync_ExistingCustomer_ShouldReturnCustomer()
    {
        // Arrange
        var created = await _sut.CreateAsync(
            "Existing Customer", "ACC123", "existing@test.com", "111222333",
            "PASAPORTE", "F", "Addr", "StmtAddr", "City", "City2", "City3", CancellationToken.None);

        // Act
        var found = await _sut.GetAsync(created.Id, CancellationToken.None);

        // Assert
        found.Should().NotBeNull();
        found!.FullName.Should().Be("Existing Customer");
        found.DocumentId.Should().Be("ACC123");
    }

    [Fact]
    public async Task GetAsync_NonExistingId_ShouldReturnNull()
    {
        // Act
        var result = await _sut.GetAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task SearchAsync_ByName_ShouldReturnMatchingCustomers()
    {
        // Arrange
        await _sut.CreateAsync("Ana Martínez", "DOC001", "ana@test.com", "111", "CEDULA", "F", "", "", "", "", "", CancellationToken.None);
        await _sut.CreateAsync("Carlos Pérez", "DOC002", "carlos@test.com", "222", "CEDULA", "M", "", "", "", "", "", CancellationToken.None);
        await _sut.CreateAsync("Ana López", "DOC003", "analop@test.com", "333", "CEDULA", "F", "", "", "", "", "", CancellationToken.None);

        // Act
        var results = await _sut.SearchAsync("Ana", 50, CancellationToken.None);

        // Assert
        results.Should().HaveCount(2);
        results.Should().OnlyContain(c => c.FullName.Contains("Ana"));
    }

    [Fact]
    public async Task SearchAsync_ByDocumentId_ShouldReturnMatch()
    {
        // Arrange
        await _sut.CreateAsync("Test User", "UNIQUE_DOC_999", "t@t.com", "000", "CEDULA", "M", "", "", "", "", "", CancellationToken.None);

        // Act
        var results = await _sut.SearchAsync("UNIQUE_DOC_999", 50, CancellationToken.None);

        // Assert
        results.Should().HaveCount(1);
        results[0].DocumentId.Should().Be("UNIQUE_DOC_999");
    }

    [Fact]
    public async Task SearchAsync_NullQuery_ShouldReturnAll()
    {
        // Arrange
        await _sut.CreateAsync("User A", "D1", "a@t.com", "1", "CEDULA", "M", "", "", "", "", "", CancellationToken.None);
        await _sut.CreateAsync("User B", "D2", "b@t.com", "2", "CEDULA", "F", "", "", "", "", "", CancellationToken.None);

        // Act
        var results = await _sut.SearchAsync(null, 50, CancellationToken.None);

        // Assert
        results.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task SearchAsync_ShouldRespectTakeLimit()
    {
        // Arrange
        for (int i = 0; i < 5; i++)
            await _sut.CreateAsync($"User {i}", $"D{i}", $"u{i}@t.com", $"{i}", "CEDULA", "M", "", "", "", "", "", CancellationToken.None);

        // Act
        var results = await _sut.SearchAsync(null, 3, CancellationToken.None);

        // Assert
        results.Should().HaveCount(3);
    }
}
