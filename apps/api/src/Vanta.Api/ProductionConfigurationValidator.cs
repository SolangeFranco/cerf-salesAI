using Vanta.Api.Modules.Identity;
using Vanta.Api.Modules.Payments;
using Vanta.Api.Modules.WhatsApp;

namespace Vanta.Api;

public static class ProductionConfigurationValidator
{
    public const string DevelopmentOnlySigningKey = "dev-only-signing-key-change-me-before-any-real-deployment-32chars";

    public static void Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        Validate(configuration, environment.IsProduction());
    }

    public static void Validate(IConfiguration configuration, bool isProduction)
    {
        if (!isProduction) return;

        var jwt = configuration.GetSection("Jwt").Get<JwtOptions>()
            ?? throw new InvalidOperationException("Jwt configuration is required in production.");
        if (string.IsNullOrWhiteSpace(jwt.Issuer) || string.IsNullOrWhiteSpace(jwt.Audience))
        {
            throw new InvalidOperationException("Jwt:Issuer and Jwt:Audience are required in production.");
        }

        if (IsWeakSigningKey(jwt.SigningKey))
        {
            throw new InvalidOperationException("A strong, non-development Jwt:SigningKey is required in production.");
        }

        var paymentProvider = configuration["Payment:Provider"];
        if (!string.Equals(paymentProvider, "manual-transfer", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A production payment provider must be configured as manual-transfer. The mock provider is development-only.");
        }

        var whatsapp = configuration.GetSection("WhatsApp").Get<WhatsAppProviderOptions>()
            ?? throw new InvalidOperationException("WhatsApp configuration is required in production.");
        if (!string.Equals(whatsapp.Provider, "meta", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(whatsapp.PhoneNumberId) ||
            string.IsNullOrWhiteSpace(whatsapp.AccessToken) ||
            string.IsNullOrWhiteSpace(whatsapp.WebhookVerifyToken) ||
            string.IsNullOrWhiteSpace(whatsapp.AppSecret))
        {
            throw new InvalidOperationException("Meta WhatsApp requires Provider, PhoneNumberId, AccessToken, WebhookVerifyToken and AppSecret in production.");
        }

        var ai = configuration.GetSection("Ai").Get<AiProviderOptions>()
            ?? throw new InvalidOperationException("Ai configuration is required in production.");
        if (!IsSupportedExternalAiProvider(ai.Provider) || string.IsNullOrWhiteSpace(ai.ApiKey))
        {
            throw new InvalidOperationException("A supported non-mock AI provider and Ai:ApiKey are required in production.");
        }

        if (!Uri.TryCreate(configuration["OpenTelemetry:OtlpEndpoint"], UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("OpenTelemetry:OtlpEndpoint is required in production.");
        }

        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString) ||
            connectionString.Contains("vanta_dev_only", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("Host=localhost", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A non-development ConnectionStrings:Default is required in production.");
        }

        if (!Uri.TryCreate(configuration["Commerce:BaseUrl"], UriKind.Absolute, out var commerceBaseUrl) ||
            commerceBaseUrl.Scheme != Uri.UriSchemeHttps || commerceBaseUrl.IsLoopback)
        {
            throw new InvalidOperationException("Commerce:BaseUrl must be an external HTTPS URL in production.");
        }
    }

    private static bool IsWeakSigningKey(string? signingKey)
    {
        if (string.IsNullOrWhiteSpace(signingKey) || signingKey.Length < 32)
            return true;

        var normalized = signingKey.Trim().ToLowerInvariant();
        if (normalized == DevelopmentOnlySigningKey ||
            normalized.Contains("change-me", StringComparison.Ordinal) ||
            normalized.Contains("replace", StringComparison.Ordinal) ||
            normalized.Contains("placeholder", StringComparison.Ordinal) ||
            normalized.Contains("your-signing", StringComparison.Ordinal))
        {
            return true;
        }

        return normalized.Distinct().Count() < 10;
    }

    private static bool IsSupportedExternalAiProvider(string? provider) =>
        provider is not null &&
        (provider.Equals("gemini", StringComparison.OrdinalIgnoreCase) ||
         provider.Equals("groq", StringComparison.OrdinalIgnoreCase) ||
         provider.Equals("openrouter", StringComparison.OrdinalIgnoreCase));
}
