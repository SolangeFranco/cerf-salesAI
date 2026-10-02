namespace Vanta.Api.Modules.Jobs;

public static class BackgroundJobType
{
    public const string WhatsAppDelivery = "whatsapp_delivery";
    public const string OrderExpiration = "order_expiration";
}

public static class BackgroundJobStatus
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
}

public sealed class BackgroundJob
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public required string Type { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string Payload { get; init; }
    public required string Status { get; set; }
    public int Attempts { get; set; }
    public int MaxAttempts { get; init; }
    public DateTimeOffset AvailableAt { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public string? WorkerId { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; set; }
}