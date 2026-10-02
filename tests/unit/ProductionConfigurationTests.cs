using Microsoft.Extensions.Configuration;
using Vanta.Api;
using Xunit;

namespace Vanta.Api.UnitTests;

public sealed class ProductionConfigurationTests
{
    [Fact]
    public void Production_rejects_the_mock_payment_provider()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "vanta-api",
            ["Jwt:Audience"] = "vanta-clients",
            ["Jwt:SigningKey"] = StrongSigningKey,
            ["Payment:Provider"] = "mock",
            ["WhatsApp:Provider"] = "meta",
            ["WhatsApp:PhoneNumberId"] = "phone",
            ["WhatsApp:AccessToken"] = "token",
            ["WhatsApp:WebhookVerifyToken"] = "verify",
            ["WhatsApp:AppSecret"] = "secret",
            ["Ai:Provider"] = "gemini",
            ["Ai:ApiKey"] = "key",
            ["OpenTelemetry:OtlpEndpoint"] = "https://otel.example.com/v1",
        });

        var exception = Assert.Throws<InvalidOperationException>(() => ProductionConfigurationValidator.Validate(configuration, true));

        Assert.Contains("payment provider", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Production_rejects_missing_provider_credentials()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "vanta-api",
            ["Jwt:Audience"] = "vanta-clients",
            ["Jwt:SigningKey"] = StrongSigningKey,
            ["Payment:Provider"] = "manual-transfer",
            ["WhatsApp:Provider"] = "meta",
            ["Ai:Provider"] = "gemini",
        });

        var exception = Assert.Throws<InvalidOperationException>(() => ProductionConfigurationValidator.Validate(configuration, true));

        Assert.Contains("WhatsApp", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Production_accepts_complete_manual_transfer_configuration()
    {
        var configuration = Configuration(CompleteProductionConfiguration());

        ProductionConfigurationValidator.Validate(configuration, true);
    }

    [Theory]
    [InlineData("Host=localhost;Database=vanta;Username=vanta;Password=secret")]
    [InlineData("Host=postgres;Database=vanta;Username=vanta;Password=vanta_dev_only")]
    public void Production_rejects_development_database_configuration(string connectionString)
    {
        var values = CompleteProductionConfiguration();
        values["ConnectionStrings:Default"] = connectionString;

        var exception = Assert.Throws<InvalidOperationException>(() => ProductionConfigurationValidator.Validate(Configuration(values), true));

        Assert.Contains("ConnectionStrings:Default", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("http://localhost:3000")]
    [InlineData("http://vanta.example.com")]
    public void Production_rejects_non_external_https_commerce_url(string baseUrl)
    {
        var values = CompleteProductionConfiguration();
        values["Commerce:BaseUrl"] = baseUrl;

        var exception = Assert.Throws<InvalidOperationException>(() => ProductionConfigurationValidator.Validate(Configuration(values), true));

        Assert.Contains("Commerce:BaseUrl", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(ProductionConfigurationValidator.DevelopmentOnlySigningKey)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("production-signing-key-change-me-before-deploy")]
    public void Production_rejects_placeholder_or_weak_jwt_signing_keys(string signingKey)
    {
        var configuration = Configuration(CompleteProductionConfiguration(signingKey: signingKey));

        var exception = Assert.Throws<InvalidOperationException>(() => ProductionConfigurationValidator.Validate(configuration, true));

        Assert.Contains("Jwt:SigningKey", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Production_rejects_unknown_or_mock_ai_provider()
    {
        var configuration = Configuration(CompleteProductionConfiguration(aiProvider: "mock"));

        var exception = Assert.Throws<InvalidOperationException>(() => ProductionConfigurationValidator.Validate(configuration, true));

        Assert.Contains("AI provider", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Development_allows_mock_configuration()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = ProductionConfigurationValidator.DevelopmentOnlySigningKey,
            ["Payment:Provider"] = "mock",
            ["WhatsApp:Provider"] = "mock",
            ["Ai:Provider"] = "mock",
        });

        ProductionConfigurationValidator.Validate(configuration, false);
    }

    private static IConfiguration Configuration(IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static Dictionary<string, string?> CompleteProductionConfiguration(
        string signingKey = StrongSigningKey,
        string aiProvider = "gemini") => new()
    {
        ["Jwt:Issuer"] = "vanta-api",
        ["Jwt:Audience"] = "vanta-clients",
        ["Jwt:SigningKey"] = signingKey,
        ["Payment:Provider"] = "manual-transfer",
        ["WhatsApp:Provider"] = "meta",
        ["WhatsApp:PhoneNumberId"] = "phone",
        ["WhatsApp:AccessToken"] = "token",
        ["WhatsApp:WebhookVerifyToken"] = "verify",
        ["WhatsApp:AppSecret"] = "secret",
        ["Ai:Provider"] = aiProvider,
        ["Ai:ApiKey"] = "key",
        ["OpenTelemetry:OtlpEndpoint"] = "https://otel.example.com/v1",
        ["ConnectionStrings:Default"] = "Host=db.internal;Database=vanta;Username=vanta_app;Password=strong-secret",
        ["Commerce:BaseUrl"] = "https://commerce.example.com",
    };

    private const string StrongSigningKey = "production-signing-key-with-at-least-32-characters";
}
