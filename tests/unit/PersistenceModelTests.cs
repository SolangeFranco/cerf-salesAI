using Microsoft.EntityFrameworkCore;
using Vanta.Api.Data;
using Vanta.Api.Modules.Catalog;
using Vanta.Api.Modules.Orders;
using Vanta.Api.Modules.Payments;
using Vanta.Api.Modules.WhatsApp;
using Xunit;

namespace Vanta.Api.UnitTests;

public sealed class PersistenceModelTests
{
    [Fact]
    public void Product_variant_relationship_is_tenant_scoped()
    {
        using var context = CreateContext();
        var foreignKey = context.Model.FindEntityType(typeof(ProductVariant))!
            .GetForeignKeys()
            .Single(key => key.PrincipalEntityType.ClrType == typeof(Product));

        Assert.Equal([nameof(ProductVariant.TenantId), nameof(ProductVariant.ProductId)], foreignKey.Properties.Select(property => property.Name));
        Assert.Equal([nameof(Product.TenantId), nameof(Product.Id)], foreignKey.PrincipalKey.Properties.Select(property => property.Name));
    }

    [Fact]
    public void Critical_query_indexes_are_present()
    {
        using var context = CreateContext();

        AssertIndex<ProductVariant>(context, false, nameof(ProductVariant.TenantId), nameof(ProductVariant.ProductId), nameof(ProductVariant.IsActive));
        AssertIndex<Order>(context, false, nameof(Order.TenantId), nameof(Order.Status), nameof(Order.CreatedAt));
        AssertIndex<Payment>(context, false, nameof(Payment.TenantId), nameof(Payment.Provider), nameof(Payment.ProviderReference));
        AssertIndex<ConversationMessage>(context, false, nameof(ConversationMessage.TenantId), nameof(ConversationMessage.ConversationId), nameof(ConversationMessage.CreatedAt));
    }

    private static void AssertIndex<TEntity>(VantaDbContext context, bool unique, params string[] properties)
        where TEntity : class
    {
        var indexes = context.Model.FindEntityType(typeof(TEntity))!.GetIndexes();
        Assert.Contains(indexes, index => index.IsUnique == unique && index.Properties.Select(property => property.Name).SequenceEqual(properties));
    }

    private static VantaDbContext CreateContext() => new(
        new DbContextOptionsBuilder<VantaDbContext>()
            .UseInMemoryDatabase($"persistence-model-{Guid.NewGuid():N}")
            .Options);
}
