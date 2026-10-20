using System.Reflection;
using OrderService.Application.Orders;
using OrderService.Domain.Orders;
using OrderService.Infrastructure.Persistence;
using Xunit;

namespace OrderService.Architecture.Tests;

public sealed class DependencyTests
{
    [Fact]
    public void DomainHasNoProjectDependencies()
    {
        var references = typeof(Order).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();

        Assert.DoesNotContain("OrderService.Application", references);
        Assert.DoesNotContain("OrderService.Infrastructure", references);
        Assert.DoesNotContain("OrderService.Api", references);
        Assert.DoesNotContain(references, name => name?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void ApplicationDependsOnDomainButNotOuterLayers()
    {
        var references = typeof(CreateOrderHandler).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();

        Assert.Contains("OrderService.Domain", references);
        Assert.DoesNotContain("OrderService.Infrastructure", references);
        Assert.DoesNotContain("OrderService.Api", references);
    }

    [Fact]
    public void InfrastructureDoesNotDependOnApi()
    {
        var references = typeof(OrderDbContext).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();

        Assert.DoesNotContain("OrderService.Api", references);
    }
}
