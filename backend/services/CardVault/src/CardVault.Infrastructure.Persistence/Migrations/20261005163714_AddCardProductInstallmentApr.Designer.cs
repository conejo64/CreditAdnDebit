using CardVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardVault.Infrastructure.Persistence.Migrations
{
    // Target-model snapshot intentionally omitted: the chain is rebaselined in Gate 0 task T7
    // and CardVaultDbContextModelSnapshot.cs already carries the two new columns.
    [DbContext(typeof(CardVaultDbContext))]
    [Migration("20261005163714_AddCardProductInstallmentApr")]
    partial class AddCardProductInstallmentApr
    {
    }
}
