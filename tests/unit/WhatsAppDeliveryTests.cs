using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vanta.Api.Data;
using Vanta.Api.Modules.WhatsApp;

namespace Vanta.Api.UnitTests;

public sealed class WhatsAppDeliveryTests
{
    [Fact]
    public async Task Meta_sender_falls_back_to_text_when_image_upload_fails()
    {
        var handler = new SequenceHandler(
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"error\":{\"message\":\"Authentication Error\",\"code\":190}}")
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"messages\":[{\"id\":\"meta-text-abc\"}]}")
            });

        var sender = new MetaWhatsAppSender(
            new HttpClient(handler),
            Options.Create(new WhatsAppProviderOptions
            {
                GraphApiBaseUrl = "https://graph.facebook.com/v22.0",
                PhoneNumberId = "123456789",
                AccessToken = "test-token"
            }),
            NullLogger<MetaWhatsAppSender>.Instance);

        var result = await sender.SendAsync(
            new WhatsAppOutboundMessage("595985832928", "Encontré 1 opción(es): Camisa Azul (125,000 Gs.)", new byte[] { 1, 2, 3 }, "image/jpeg", "tenant-phone-id"),
            CancellationToken.None);

        Assert.Equal("meta-text-abc", result.ProviderMessageId);
        Assert.All(handler.RequestUris, uri => Assert.Contains("/tenant-phone-id/", uri, StringComparison.Ordinal));
    }
    [Fact]
    public async Task Pending_message_is_marked_sent_after_provider_success()
    {
        await using var db = CreateDb();
        var message = await SeedPendingMessageAsync(db);
        var sender = new FakeSender();
        var service = new WhatsAppDeliveryService(db, sender, NullLogger<WhatsAppDeliveryService>.Instance);

        var processed = await service.DeliverPendingAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        Assert.Equal("Sent", (await db.ConversationMessages.FindAsync(message.Id))!.DeliveryStatus);
        Assert.Single(sender.Messages);
    }

    [Fact]
    public async Task Failed_delivery_is_retryable_and_increments_attempts()
    {
        await using var db = CreateDb();
        var message = await SeedPendingMessageAsync(db);
        var sender = new FakeSender { Failure = new HttpRequestException("provider unavailable") };
        var service = new WhatsAppDeliveryService(db, sender, NullLogger<WhatsAppDeliveryService>.Instance);

        await service.DeliverPendingAsync(CancellationToken.None);

        var stored = (await db.ConversationMessages.FindAsync(message.Id))!;
        Assert.Equal("Pending", stored.DeliveryStatus);
        Assert.Equal(1, stored.DeliveryAttempts);
        Assert.NotNull(stored.NextDeliveryAttemptAt);
    }

    [Fact]
    public async Task Fifth_failed_delivery_is_marked_failed()
    {
        await using var db = CreateDb();
        var message = await SeedPendingMessageAsync(db);
        message.DeliveryAttempts = 4;
        var sender = new FakeSender { Failure = new InvalidOperationException("provider unavailable") };
        var service = new WhatsAppDeliveryService(db, sender, NullLogger<WhatsAppDeliveryService>.Instance);

        await service.DeliverPendingAsync(CancellationToken.None);

        var stored = (await db.ConversationMessages.FindAsync(message.Id))!;
        Assert.Equal("Failed", stored.DeliveryStatus);
        Assert.Equal(5, stored.DeliveryAttempts);
    }

    private static VantaDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<VantaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new VantaDbContext(options);
    }

    private static async Task<ConversationMessage> SeedPendingMessageAsync(VantaDbContext db)
    {
        var tenantId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        db.Conversations.Add(new Conversation
        {
            Id = conversationId,
            TenantId = tenantId,
            ChannelAccountId = Guid.NewGuid(),
            CustomerExternalId = "customer-test-001",
            Status = "Open",
            LastMessageAt = DateTimeOffset.UtcNow,
        });
        var message = new ConversationMessage
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ConversationId = conversationId,
            ExternalMessageId = $"delivery-test-{Guid.NewGuid():N}",
            Direction = "Outbound",
            Text = "Actualización de pedido",
            DeliveryStatus = "Pending",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ConversationMessages.Add(message);
        await db.SaveChangesAsync();
        return message;
    }

    private sealed class FakeSender : IWhatsAppMessageSender
    {
        public List<WhatsAppOutboundMessage> Messages { get; } = [];
        public Exception? Failure { get; init; }

        public Task<WhatsAppSendResult> SendAsync(WhatsAppOutboundMessage message, CancellationToken cancellationToken)
        {
            if (Failure is not null) throw Failure;
            Messages.Add(message);
            return Task.FromResult(new WhatsAppSendResult($"fake-{message.RecipientExternalId}"));
        }
    }

    private sealed class SequenceHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public List<string> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No more mocked responses available.");
            }

            RequestUris.Add(request.RequestUri?.AbsoluteUri ?? string.Empty);
            return Task.FromResult(_responses.Dequeue());
        }
    }
}
