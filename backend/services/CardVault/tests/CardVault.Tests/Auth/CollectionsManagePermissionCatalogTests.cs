using CardVault.Api.Security;
using FluentAssertions;

namespace CardVault.Tests.Auth;

/// <summary>
/// TDD tests for PermissionCatalog.CollectionsManage entry.
/// RED: written before the constant exists in PermissionCatalog.
/// </summary>
public sealed class CollectionsManagePermissionCatalogTests
{
    [Fact]
    public void CollectionsManage_ShouldHaveExpectedValue()
    {
        PermissionCatalog.CollectionsManage.Should().Be("collections:manage");
    }

    [Fact]
    public void CollectionsManage_ShouldBeInAllList()
    {
        PermissionCatalog.All.Should().Contain(PermissionCatalog.CollectionsManage);
    }

    [Fact]
    public void CollectionsManage_ShouldHaveDescription()
    {
        PermissionCatalog.Descriptions.Should().ContainKey(PermissionCatalog.CollectionsManage);
        PermissionCatalog.Descriptions[PermissionCatalog.CollectionsManage].Should().NotBeNullOrWhiteSpace();
    }
}
