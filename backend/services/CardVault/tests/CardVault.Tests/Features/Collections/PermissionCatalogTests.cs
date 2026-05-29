using CardVault.Api.Security;
using FluentAssertions;

namespace CardVault.Tests.Features.Collections;

/// <summary>
/// TDD tests for PermissionCatalog.CollectionsView entry.
/// RED: written before the constant exists.
/// </summary>
public sealed class PermissionCatalogTests
{
    [Fact]
    public void CollectionsView_ShouldHaveExpectedValue()
    {
        PermissionCatalog.CollectionsView.Should().Be("collections:view");
    }

    [Fact]
    public void CollectionsView_ShouldBeInAllList()
    {
        PermissionCatalog.All.Should().Contain(PermissionCatalog.CollectionsView);
    }

    [Fact]
    public void CollectionsView_ShouldHaveDescription()
    {
        PermissionCatalog.Descriptions.Should().ContainKey(PermissionCatalog.CollectionsView);
        PermissionCatalog.Descriptions[PermissionCatalog.CollectionsView].Should().NotBeNullOrWhiteSpace();
    }
}
