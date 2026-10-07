using System.Reflection;
using FluentAssertions;

namespace IsoSwitch.IntegrationTests.Infrastructure;

/// <summary>
/// xunit 2 has no assembly-level trait, so the <c>Category=Integration</c> contract that CI relies on
/// (<c>--filter "Category!=Integration"</c> on the solution run) is enforced here: every test class in
/// this assembly must carry the trait, directly or through its base class, and must join the shared
/// PostgreSQL collection so a single server is started per run.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
[Trait(Category.TraitName, Category.Integration)]
public sealed class CategoryTraitEnforcementTests
{
    [Fact(DisplayName = "Every test class in this assembly is tagged Category=Integration and joins the Postgres collection")]
    public void Every_test_class_carries_the_integration_trait()
    {
        var testClasses = typeof(CategoryTraitEnforcementTests).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Any(m => m.GetCustomAttributes<FactAttribute>(inherit: true).Any()))
            .ToList();

        testClasses.Should().NotBeEmpty();

        var untagged = testClasses
            .Where(t => !HasIntegrationTrait(t))
            .Select(t => t.FullName)
            .ToList();
        untagged.Should().BeEmpty("CI excludes this project from the solution run by trait; an untagged class would run without PostgreSQL");

        var outsideCollection = testClasses
            .Where(t => t.GetCustomAttribute<CollectionAttribute>(inherit: true) is null)
            .Select(t => t.FullName)
            .ToList();
        outsideCollection.Should().BeEmpty("every class must share the single PostgresFixture collection");
    }

    // xunit 2's TraitAttribute keeps its name/value private, so the constructor arguments are read
    // from attribute metadata, walking up the base classes (xunit collects class traits with inheritance).
    private static bool HasIntegrationTrait(Type testClass)
    {
        for (var type = testClass; type is not null; type = type.BaseType)
        {
            var tagged = type.GetCustomAttributesData()
                .Where(a => a.AttributeType == typeof(TraitAttribute) && a.ConstructorArguments.Count == 2)
                .Any(a => Equals(a.ConstructorArguments[0].Value, Category.TraitName)
                       && Equals(a.ConstructorArguments[1].Value, Category.Integration));
            if (tagged)
            {
                return true;
            }
        }

        return false;
    }
}
