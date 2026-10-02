using Microsoft.Extensions.Options;
using Vanta.Api.Modules.WhatsApp;

namespace Vanta.Api.UnitTests;

public sealed class MetaWebhookParserTests
{
  [Fact]
  public void Linked_device_contact_update_preserves_name_and_profile_image()
  {
    var provider = new LinkedDeviceWhatsAppProvider(Options.Create(new WhatsAppProviderOptions()));
    var signal = new WhatsAppWebhookSignal(
      """
      {
        "type": "contact",
        "sessionId": "session-001",
        "eventId": "contact-event-001",
        "contactId": "595981000001",
        "contactName": "Ana Pérez",
        "contactImageUrl": "https://example.test/ana.jpg"
      }
      """,
      new Dictionary<string, string>());

    var contact = Assert.IsType<WhatsAppContactUpdateEvent>(Assert.Single(provider.ParseWebhook(signal)));

    Assert.Equal("Ana Pérez", contact.CustomerName);
    Assert.Equal("https://example.test/ana.jpg", contact.CustomerImageUrl);
  }

  [Fact]
  public void Linked_device_chat_and_reaction_events_are_normalized()
  {
    var provider = new LinkedDeviceWhatsAppProvider(Options.Create(new WhatsAppProviderOptions()));
    var chat = Assert.IsType<WhatsAppChatUpdateEvent>(Assert.Single(provider.ParseWebhook(new WhatsAppWebhookSignal(
      """
      { "type": "chat", "sessionId": "session-001", "eventId": "chat-001", "contactId": "595981000001", "contactName": "Ana Pérez", "contactImageUrl": "https://example.test/ana.jpg", "createdAt": "2026-09-22T12:00:00Z" }
      """, new Dictionary<string, string>()))));
    var reaction = Assert.IsType<WhatsAppReactionEvent>(Assert.Single(provider.ParseWebhook(new WhatsAppWebhookSignal(
      """
      { "type": "reaction", "sessionId": "session-001", "eventId": "reaction-001", "messageId": "message-001", "emoji": "👍", "sender": "595981000001" }
      """, new Dictionary<string, string>()))));

    Assert.Equal("Ana Pérez", chat.CustomerName);
    Assert.Equal("https://example.test/ana.jpg", chat.CustomerImageUrl);
    Assert.Equal("message-001", reaction.MessageExternalId);
    Assert.Equal("👍", reaction.Emoji);
  }

  [Fact]
  public void Linked_device_message_preserves_audio_attachment_metadata()
  {
    var provider = new LinkedDeviceWhatsAppProvider(Options.Create(new WhatsAppProviderOptions()));
    var message = Assert.IsType<WhatsAppInboundMessageEvent>(Assert.Single(provider.ParseWebhook(new WhatsAppWebhookSignal(
      """
      { "type": "message", "sessionId": "session-001", "eventId": "message-001", "messageId": "message-001", "from": "595981000001", "text": "", "mediaBase64": "AQID", "mediaMimeType": "audio/ogg; codecs=opus", "mediaKind": "audio", "createdAt": "2026-09-22T12:00:00Z" }
      """, new Dictionary<string, string>()))));

    Assert.Equal("AQID", message.InlineMediaBase64);
    Assert.Equal("audio/ogg; codecs=opus", message.InlineMediaMimeType);
    Assert.Equal("audio", message.InlineMediaKind);
  }

  [Fact]
  public void Linked_device_identity_mapping_preserves_canonical_phone_number()
  {
    var provider = new LinkedDeviceWhatsAppProvider(Options.Create(new WhatsAppProviderOptions()));
    var identity = Assert.IsType<WhatsAppIdentityMappingEvent>(Assert.Single(provider.ParseWebhook(new WhatsAppWebhookSignal(
      """
      { "type": "identity", "sessionId": "session-001", "eventId": "identity-001", "sourceId": "134960790941769", "contactId": "595994444784", "contactName": "Comercio Demo" }
      """, new Dictionary<string, string>()))));

    Assert.Equal("134960790941769", identity.SourceExternalId);
    Assert.Equal("595994444784", identity.CustomerExternalId);
    Assert.Equal("Comercio Demo", identity.CustomerName);
  }

    [Fact]
    public void Parses_delivery_status_and_error()
    {
        const string payload = """
        {
          "entry": [{
            "changes": [{
              "value": {
                "statuses": [{
                  "id": "wamid.OUT-001",
                  "status": "failed",
                  "errors": [{ "code": 131026, "title": "Message undeliverable" }]
                }]
              }
            }]
          }]
        }
        """;

        var result = MetaWebhookParser.ParseDeliveryStatuses(payload);

        var status = Assert.Single(result);
        Assert.Equal("wamid.OUT-001", status.MessageId);
        Assert.Equal("failed", status.Status);
        Assert.Contains("131026", status.Error);
    }

    [Fact]
    public void Delivery_status_does_not_regress()
    {
        Assert.False(MetaWebhookParser.ShouldAdvanceDeliveryStatus("Read", "Delivered"));
        Assert.True(MetaWebhookParser.ShouldAdvanceDeliveryStatus("Sent", "Delivered"));
        Assert.True(MetaWebhookParser.ShouldAdvanceDeliveryStatus("Sent", "Failed"));
    }

    [Fact]
    public void Parses_text_message_from_meta_payload()
    {
        const string payload = """
        {
          "object": "whatsapp_business_account",
          "entry": [{
            "id": "business-account-001",
            "changes": [{
              "field": "messages",
              "value": {
                "messaging_product": "whatsapp",
                "metadata": { "phone_number_id": "phone-001" },
                "contacts": [{ "wa_id": "595981000001" }],
                "messages": [{
                  "from": "595981000001",
                  "id": "wamid.TEST-001",
                  "timestamp": "1710000000",
                  "text": { "body": "Quiero comprar la camisa azul" },
                  "type": "text"
                }]
              }
            }]
          }]
        }
        """;

        var result = MetaWebhookParser.ParseTextMessage(payload);

        Assert.NotNull(result);
        Assert.Equal("wamid.TEST-001", result!.MessageId);
        Assert.Equal("595981000001", result.CustomerExternalId);
        Assert.Equal("Quiero comprar la camisa azul", result.Text);
        Assert.Equal("phone-001", result.PhoneNumberId);
    }

    [Fact]
    public void Parses_image_media_id_without_downloading_it()
    {
        const string payload = """
        {
          "entry": [{
            "changes": [{
              "value": {
                "messages": [{
                  "from": "595981000001",
                  "id": "wamid.MEDIA-001",
                  "type": "image",
                  "image": { "id": "media-001" }
                }]
              }
            }]
          }]
        }
        """;

        var result = MetaWebhookParser.ParseTextMessage(payload);

        Assert.NotNull(result);
        Assert.Equal("wamid.MEDIA-001", result!.MessageId);
        Assert.Equal("595981000001", result.CustomerExternalId);
        Assert.Equal("media-001", result.MediaId);
        Assert.Equal("image", result.MediaType);
        Assert.Empty(result.Text);
    }
}
