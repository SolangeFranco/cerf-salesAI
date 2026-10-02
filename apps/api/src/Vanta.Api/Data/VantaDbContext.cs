using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Vanta.Api.Modules.Analytics;
using Vanta.Api.Modules.Catalog;
using Vanta.Api.Modules.Customers;
using Vanta.Api.Modules.Identity;
using Vanta.Api.Modules.Inventory;
using Vanta.Api.Modules.Jobs;
using Vanta.Api.Modules.Knowledge;
using Vanta.Api.Modules.Orders;
using Vanta.Api.Modules.Payments;
using Vanta.Api.Modules.WhatsApp;
using Vanta.Api.Modules.Tenants;

namespace Vanta.Api.Data;

public sealed class VantaDbContext(DbContextOptions<VantaDbContext> options)
    : IdentityDbContext<VantaUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<StockAlertRequest> StockAlertRequests => Set<StockAlertRequest>();
    public DbSet<StockReservation> StockReservations => Set<StockReservation>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderStatusHistory> OrderStatusHistory => Set<OrderStatusHistory>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentWebhookEvent> PaymentWebhookEvents => Set<PaymentWebhookEvent>();
    public DbSet<PaymentStatusHistory> PaymentStatusHistory => Set<PaymentStatusHistory>();
    public DbSet<WhatsAppChannelAccount> WhatsAppChannelAccounts => Set<WhatsAppChannelAccount>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationMessage> ConversationMessages => Set<ConversationMessage>();
    public DbSet<ConversationActivity> ConversationActivities => Set<ConversationActivity>();
    public DbSet<WhatsAppWebhookEvent> WhatsAppWebhookEvents => Set<WhatsAppWebhookEvent>();
    public DbSet<KnowledgeDocument> KnowledgeDocuments => Set<KnowledgeDocument>();
    public DbSet<KnowledgeChunk> KnowledgeChunks => Set<KnowledgeChunk>();
    public DbSet<KnowledgeEmbedding> KnowledgeEmbeddings => Set<KnowledgeEmbedding>();
    public DbSet<CommerceEvent> CommerceEvents => Set<CommerceEvent>();
    public DbSet<BackgroundJob> BackgroundJobs => Set<BackgroundJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.ToTable("tenants");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Name).HasMaxLength(200).IsRequired();
            entity.Property(t => t.Slug).HasMaxLength(100).IsRequired();
            entity.Property(t => t.LogoUrl).HasMaxLength(2048);
            entity.Property(t => t.StoreSetupCompleted).IsRequired();
            entity.Property(t => t.AiResponsesEnabled).HasDefaultValue(false).IsRequired();
            entity.Property(t => t.BankName).HasMaxLength(120);
            entity.Property(t => t.BankBeneficiaryName).HasMaxLength(200);
            entity.Property(t => t.BankAccountNumber).HasMaxLength(80);
            entity.Property(t => t.BankDocumentNumber).HasMaxLength(40);
            entity.Property(t => t.BankAlias).HasMaxLength(120);
            entity.Property(t => t.DeliveryBaseCostGs).HasPrecision(18, 2).IsRequired();
            entity.Property(t => t.DeliveryIncludedDistanceKm).HasPrecision(10, 2).IsRequired();
            entity.Property(t => t.DeliveryExtraCostPerKmGs).HasPrecision(18, 2).IsRequired();
            entity.Property(t => t.MetropolitanDeliveryCostGs).HasPrecision(18, 2).IsRequired();
            entity.Property(t => t.InteriorDeliveryCostGs).HasPrecision(18, 2).IsRequired();
            entity.ToTable(table => table.HasCheckConstraint("CK_tenants_delivery_costs_non_negative", "\"MetropolitanDeliveryCostGs\" >= 0 AND \"InteriorDeliveryCostGs\" >= 0 AND \"DeliveryBaseCostGs\" >= 0 AND \"DeliveryIncludedDistanceKm\" > 0 AND \"DeliveryExtraCostPerKmGs\" >= 0"));
            entity.HasIndex(t => t.Slug).IsUnique();
        });

        modelBuilder.Entity<VantaUser>(entity =>
        {
            entity.ToTable("users");
            entity.HasIndex(u => u.TenantId);
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("products");
            entity.HasKey(product => product.Id);
            entity.Property(product => product.Name).HasMaxLength(200).IsRequired();
            entity.Property(product => product.Description).HasMaxLength(2000);
            entity.Property(product => product.ProductNumber).HasMaxLength(40).IsRequired();
            entity.Property(product => product.Sku).HasMaxLength(100).IsRequired();
            entity.Property(product => product.Price).HasPrecision(18, 2).IsRequired();
            entity.Property(product => product.DiscountPercent).HasPrecision(5, 2).IsRequired();
            entity.Property(product => product.AvailableSizes).HasColumnType("jsonb").IsRequired();
            entity.Property(product => product.Gender).HasMaxLength(20).IsRequired();
            entity.HasIndex(product => new { product.TenantId, product.Sku }).IsUnique();
            entity.HasIndex(product => new { product.TenantId, product.ProductNumber }).IsUnique();
            entity.HasIndex(product => new { product.TenantId, product.IsActive });
            entity.HasAlternateKey(product => new { product.TenantId, product.Id });
            entity.HasOne(product => product.Category)
                .WithMany()
                .HasForeignKey(product => new { product.TenantId, product.CategoryId })
                .HasPrincipalKey(category => new { category.TenantId, category.Id })
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasMany(product => product.Images)
                .WithOne()
                .HasForeignKey(image => new { image.TenantId, image.ProductId })
                .HasPrincipalKey(product => new { product.TenantId, product.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProductImage>(entity =>
        {
            entity.ToTable("product_images");
            entity.HasKey(image => image.Id);
            entity.Property(image => image.Data).IsRequired();
            entity.Property(image => image.MediaType).HasMaxLength(100).IsRequired();
            entity.HasIndex(image => new { image.TenantId, image.ProductId, image.SortOrder });
        });

        modelBuilder.Entity<ProductCategory>(entity =>
        {
            entity.ToTable("product_categories");
            entity.HasKey(category => category.Id);
            entity.Property(category => category.Name).HasMaxLength(100).IsRequired();
            entity.HasIndex(category => new { category.TenantId, category.Name }).IsUnique();
            entity.HasAlternateKey(category => new { category.TenantId, category.Id });
        });

        modelBuilder.Entity<ProductVariant>(entity =>
        {
            entity.ToTable("product_variants");
            entity.HasKey(variant => variant.Id);
            entity.Property(variant => variant.Name).HasMaxLength(200).IsRequired();
            entity.Property(variant => variant.Sku).HasMaxLength(100).IsRequired();
            entity.Property(variant => variant.ReservedStock).IsRequired();
            entity.Property(variant => variant.PriceOverride).HasPrecision(18, 2);
            entity.ToTable(table => table.HasCheckConstraint("CK_product_variants_stock_non_negative", "\"StockQuantity\" >= 0 AND \"ReservedStock\" >= 0 AND \"ReservedStock\" <= \"StockQuantity\""));
            entity.HasIndex(variant => new { variant.TenantId, variant.Sku }).IsUnique();
            entity.HasIndex(variant => new { variant.TenantId, variant.ProductId, variant.IsActive });
            entity.HasOne<Product>()
                .WithMany()
                .HasForeignKey(variant => new { variant.TenantId, variant.ProductId })
                .HasPrincipalKey(product => new { product.TenantId, product.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StockAlertRequest>(entity =>
        {
            entity.ToTable("stock_alert_requests");
            entity.HasKey(alert => alert.Id);
            entity.HasIndex(alert => new { alert.TenantId, alert.CustomerId, alert.ProductId, alert.IsActive }).IsUnique();
        });

        modelBuilder.Entity<StockReservation>(entity =>
        {
            entity.ToTable("stock_reservations");
            entity.HasKey(reservation => reservation.Id);
            entity.Property(reservation => reservation.Status).HasMaxLength(20).IsRequired();
            entity.HasIndex(reservation => new { reservation.TenantId, reservation.OrderId, reservation.Status });
            entity.HasIndex(reservation => reservation.OrderItemId).IsUnique();
        });

        modelBuilder.Entity<InventoryMovement>(entity =>
        {
            entity.ToTable("inventory_movements");
            entity.HasKey(movement => movement.Id);
            entity.Property(movement => movement.Type).HasMaxLength(20).IsRequired();
            entity.HasIndex(movement => new { movement.TenantId, movement.ProductVariantId, movement.CreatedAt });
            entity.HasIndex(movement => new { movement.TenantId, movement.OrderId, movement.Type });
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("orders");
            entity.HasKey(order => order.Id);
            entity.Property(order => order.Number).HasMaxLength(40).IsRequired();
            entity.Property(order => order.IdempotencyKey).HasMaxLength(200).IsRequired();
            entity.Property(order => order.Status).HasMaxLength(40).IsRequired();
            entity.Property(order => order.Total).HasPrecision(18, 2).IsRequired();
            entity.HasIndex(order => new { order.TenantId, order.IdempotencyKey }).IsUnique();
            entity.HasIndex(order => new { order.TenantId, order.CreatedAt });
            entity.HasIndex(order => new { order.TenantId, order.Status, order.CreatedAt });
            entity.HasIndex(order => new { order.Status, order.ReservationExpiresAt });
            entity.HasOne<Customer>()
                .WithMany()
                .HasForeignKey(order => order.CustomerId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<Conversation>()
                .WithMany()
                .HasForeignKey(order => order.ConversationId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(order => new { order.TenantId, order.ConversationId });
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.ToTable("order_items");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ProductName).HasMaxLength(200).IsRequired();
            entity.Property(item => item.Sku).HasMaxLength(100).IsRequired();
            entity.Property(item => item.UnitPrice).HasPrecision(18, 2).IsRequired();
            entity.Property(item => item.LineTotal).HasPrecision(18, 2).IsRequired();
            entity.HasOne<Order>()
                .WithMany(order => order.Items)
                .HasForeignKey(item => item.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Cart>(entity =>
        {
            entity.ToTable("carts");
            entity.HasKey(cart => cart.Id);
            entity.HasIndex(cart => new { cart.TenantId, cart.ConversationId }).IsUnique();
            entity.HasMany(cart => cart.Items)
                .WithOne()
                .HasForeignKey(item => item.CartId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CartItem>(entity =>
        {
            entity.ToTable("cart_items");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ProductName).HasMaxLength(200).IsRequired();
            entity.Property(item => item.Sku).HasMaxLength(100).IsRequired();
            entity.Property(item => item.UnitPrice).HasPrecision(18, 2).IsRequired();
            entity.HasIndex(item => new { item.CartId, item.ProductId, item.VariantId }).IsUnique();
        });

            modelBuilder.Entity<OrderStatusHistory>(entity =>
            {
                entity.ToTable("order_status_history");
                entity.HasKey(history => history.Id);
                entity.Property(history => history.FromStatus).HasMaxLength(40).IsRequired();
                entity.Property(history => history.ToStatus).HasMaxLength(40).IsRequired();
                entity.HasIndex(history => new { history.TenantId, history.OrderId, history.CreatedAt });
                entity.HasOne<Order>()
                .WithMany(order => order.StatusHistory)
                .HasForeignKey(history => history.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Customer>(entity =>
            {
                entity.ToTable("customers");
                entity.HasKey(customer => customer.Id);
                entity.Property(customer => customer.Name).HasMaxLength(200).IsRequired();
                entity.Property(customer => customer.Phone).HasMaxLength(40).IsRequired();
                entity.Property(customer => customer.Email).HasMaxLength(200);
                entity.Property(customer => customer.Address).HasMaxLength(500);
                entity.Property(customer => customer.ProfileImageUrl).HasMaxLength(2048);
                entity.HasIndex(customer => new { customer.TenantId, customer.Phone }).IsUnique();
            });

            modelBuilder.Entity<Payment>(entity =>
            {
                entity.ToTable("payments");
                entity.HasKey(payment => payment.Id);
                entity.Property(payment => payment.Provider).HasMaxLength(40).IsRequired();
                entity.Property(payment => payment.ProviderReference).HasMaxLength(200).IsRequired();
                entity.Property(payment => payment.PublicReference).HasMaxLength(64).IsRequired();
                entity.Property(payment => payment.IdempotencyKey).HasMaxLength(200).IsRequired();
                entity.Property(payment => payment.Status).HasMaxLength(40).IsRequired();
                entity.Property(payment => payment.Amount).HasPrecision(18, 2).IsRequired();
                entity.HasIndex(payment => new { payment.TenantId, payment.IdempotencyKey }).IsUnique();
                entity.HasIndex(payment => new { payment.TenantId, payment.OrderId });
                entity.HasIndex(payment => new { payment.TenantId, payment.Provider, payment.ProviderReference });
                entity.HasIndex(payment => payment.PublicReference).IsUnique();
            });

            modelBuilder.Entity<PaymentStatusHistory>(entity =>
            {
                entity.ToTable("payment_status_history");
                entity.HasKey(history => history.Id);
                entity.Property(history => history.FromStatus).HasMaxLength(40).IsRequired();
                entity.Property(history => history.ToStatus).HasMaxLength(40).IsRequired();
                entity.Property(history => history.Source).HasMaxLength(40).IsRequired();
                entity.HasIndex(history => new { history.TenantId, history.PaymentId, history.CreatedAt });
                entity.HasOne<Payment>()
                    .WithMany()
                    .HasForeignKey(history => history.PaymentId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<PaymentWebhookEvent>(entity =>
            {
                entity.ToTable("payment_webhook_events");
                entity.HasKey(webhookEvent => webhookEvent.Id);
                entity.Property(webhookEvent => webhookEvent.Provider).HasMaxLength(40).IsRequired();
                entity.Property(webhookEvent => webhookEvent.EventId).HasMaxLength(200).IsRequired();
                entity.HasIndex(webhookEvent => new { webhookEvent.TenantId, webhookEvent.Provider, webhookEvent.EventId }).IsUnique();
            });

            modelBuilder.Entity<WhatsAppChannelAccount>(entity =>
            {
                entity.ToTable("whatsapp_channel_accounts"); entity.HasKey(account => account.Id);
                entity.Property(account => account.Provider).HasMaxLength(40).IsRequired();
                entity.Property(account => account.ExternalAccountId).HasMaxLength(200).IsRequired();
                entity.Property(account => account.DisplayName).HasMaxLength(200).IsRequired();
                entity.HasIndex(account => new { account.Provider, account.ExternalAccountId }).IsUnique();
            });

            modelBuilder.Entity<Conversation>(entity =>
            {
                entity.ToTable("conversations"); entity.HasKey(conversation => conversation.Id);
                entity.Property(conversation => conversation.CustomerExternalId).HasMaxLength(200).IsRequired();
                entity.Property(conversation => conversation.IsGroup).IsRequired();
                entity.Property(conversation => conversation.IsChannel).IsRequired();
                entity.Property(conversation => conversation.Status).HasMaxLength(40).IsRequired();
                entity.Property(conversation => conversation.CurrentIntent).HasMaxLength(60);
                entity.HasIndex(conversation => new { conversation.TenantId, conversation.ChannelAccountId, conversation.CustomerExternalId }).IsUnique();
                entity.HasOne<Customer>().WithMany().HasForeignKey(conversation => conversation.CustomerId).OnDelete(DeleteBehavior.SetNull);
                entity.HasIndex(conversation => new { conversation.TenantId, conversation.CustomerId });
                entity.HasOne<VantaUser>().WithMany().HasForeignKey(conversation => conversation.AssignedUserId).OnDelete(DeleteBehavior.SetNull);
                entity.HasIndex(conversation => new { conversation.TenantId, conversation.AssignedUserId });
                entity.HasIndex(conversation => new { conversation.TenantId, conversation.Status, conversation.LastActivityAt });
            });

            modelBuilder.Entity<ConversationMessage>(entity =>
            {
                entity.ToTable("conversation_messages"); entity.HasKey(message => message.Id);
                entity.Property(message => message.ExternalMessageId).HasMaxLength(200).IsRequired();
                entity.Property(message => message.Direction).HasMaxLength(20).IsRequired();
                entity.Property(message => message.Text).HasMaxLength(10000).IsRequired();
                entity.Property(message => message.DeliveryStatus).HasMaxLength(30);
                entity.Property(message => message.DeliveryError).HasMaxLength(1000);
                entity.HasIndex(message => new { message.DeliveryStatus, message.NextDeliveryAttemptAt });
                entity.HasIndex(message => new { message.TenantId, message.ExternalMessageId }).IsUnique();
                entity.HasIndex(message => new { message.TenantId, message.ConversationId, message.CreatedAt });
            });

            modelBuilder.Entity<WhatsAppWebhookEvent>(entity =>
            {
                entity.ToTable("whatsapp_webhook_events");
                entity.HasKey(webhookEvent => webhookEvent.Id);
                entity.Property(webhookEvent => webhookEvent.Provider).HasMaxLength(40).IsRequired();
                entity.Property(webhookEvent => webhookEvent.EventId).HasMaxLength(200).IsRequired();
                entity.HasIndex(webhookEvent => new { webhookEvent.TenantId, webhookEvent.Provider, webhookEvent.EventId }).IsUnique();
            });

            modelBuilder.Entity<ConversationActivity>(entity =>
            {
                entity.ToTable("conversation_activities");
                entity.HasKey(activity => activity.Id);
                entity.Property(activity => activity.Type).HasMaxLength(40).IsRequired();
                entity.Property(activity => activity.FromStatus).HasMaxLength(40);
                entity.Property(activity => activity.ToStatus).HasMaxLength(40);
                entity.Property(activity => activity.Note).HasMaxLength(1000);
                entity.HasIndex(activity => new { activity.TenantId, activity.ConversationId, activity.CreatedAt });
                entity.HasOne<Conversation>().WithMany().HasForeignKey(activity => activity.ConversationId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<KnowledgeDocument>(entity =>
            {
                entity.ToTable("knowledge_documents");
                entity.HasKey(document => document.Id);
                entity.Property(document => document.Type).HasMaxLength(40).IsRequired();
                entity.Property(document => document.Title).HasMaxLength(200).IsRequired();
                entity.Property(document => document.SourceText).HasMaxLength(20000).IsRequired();
                entity.Property(document => document.Status).HasMaxLength(20).IsRequired();
                entity.Property(document => document.ErrorMessage).HasMaxLength(500);
                entity.HasIndex(document => new { document.TenantId, document.Type });
                entity.HasIndex(document => new { document.TenantId, document.Status });
            });

            modelBuilder.Entity<KnowledgeChunk>(entity =>
            {
                entity.ToTable("knowledge_chunks");
                entity.HasKey(chunk => chunk.Id);
                entity.Property(chunk => chunk.Content).HasMaxLength(1000).IsRequired();
                entity.HasIndex(chunk => new { chunk.TenantId, chunk.DocumentId, chunk.ChunkIndex }).IsUnique();
                entity.HasOne<KnowledgeDocument>().WithMany().HasForeignKey(chunk => chunk.DocumentId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<KnowledgeEmbedding>(entity =>
            {
                entity.ToTable("knowledge_embeddings");
                entity.HasKey(embedding => embedding.Id);
                entity.Property(embedding => embedding.Provider).HasMaxLength(40).IsRequired();
                entity.Property(embedding => embedding.Model).HasMaxLength(100).IsRequired();
                entity.HasIndex(embedding => new { embedding.TenantId, embedding.ChunkId, embedding.Provider, embedding.Model }).IsUnique();
                entity.HasOne<KnowledgeChunk>().WithMany().HasForeignKey(embedding => embedding.ChunkId).OnDelete(DeleteBehavior.Cascade);
                // Unconstrained dimension: different embedding providers/models may produce
                // different vector lengths, and only rows with matching Provider/Model are ever
                // compared to each other (see KnowledgeSearchService). Only Npgsql understands the
                // `vector` column type; the InMemory test provider ignores it entirely, so
                // document/chunk CRUD stays testable without Postgres while real semantic search
                // is exercised only against Postgres (KnowledgeSemanticSearchTests).
                if (Database.IsNpgsql())
                {
                    entity.Property(embedding => embedding.Vector).HasColumnType("vector");
                }
                else
                {
                    entity.Ignore(embedding => embedding.Vector);
                }
            });

            modelBuilder.Entity<CommerceEvent>(entity =>
            {
                entity.ToTable("commerce_events");
                entity.HasKey(current => current.Id);
                entity.Property(current => current.Type).HasMaxLength(40).IsRequired();
                entity.Property(current => current.Metadata).HasMaxLength(1000);
                entity.HasIndex(current => new { current.TenantId, current.Type, current.CreatedAt });
                entity.HasIndex(current => new { current.TenantId, current.ConversationId, current.CreatedAt });
                entity.HasIndex(current => new { current.TenantId, current.OrderId });
                entity.HasIndex(current => new { current.TenantId, current.CartId });
                entity.HasIndex(current => new { current.TenantId, current.PaymentId });
            });

            modelBuilder.Entity<BackgroundJob>(entity =>
            {
                entity.ToTable("background_jobs");
                entity.HasKey(job => job.Id);
                entity.Property(job => job.Type).HasMaxLength(80).IsRequired();
                entity.Property(job => job.IdempotencyKey).HasMaxLength(200).IsRequired();
                entity.Property(job => job.Payload).HasMaxLength(4000).IsRequired();
                entity.Property(job => job.Status).HasMaxLength(20).IsRequired();
                entity.Property(job => job.WorkerId).HasMaxLength(100);
                entity.Property(job => job.LastError).HasMaxLength(1000);
                entity.HasIndex(job => new { job.TenantId, job.Type, job.IdempotencyKey }).IsUnique();
                entity.HasIndex(job => new { job.Status, job.Type, job.AvailableAt });
                entity.HasIndex(job => new { job.Status, job.LockedUntil });
            });
    }
}

