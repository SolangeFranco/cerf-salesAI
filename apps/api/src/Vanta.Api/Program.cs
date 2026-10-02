using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Vanta.Api;
using Vanta.Api.Data;
using Vanta.Api.Modules.Analytics;
using Vanta.Api.Modules.Catalog;
using Vanta.Api.Modules.Commerce;
using Vanta.Api.Modules.Customers;
using Vanta.Api.Modules.Identity;
using Vanta.Api.Modules.Inventory;
using Vanta.Api.Modules.Jobs;
using Vanta.Api.Modules.Knowledge;
using Vanta.Api.Modules.Orders;
using Vanta.Api.Modules.Observability;
using Vanta.Api.Modules.Payments;
using Vanta.Api.Modules.Tenants;
using Vanta.Api.Modules.WhatsApp;
var builder = WebApplication.CreateBuilder(args);
const long maxRequestBodySize = 15 * 1024 * 1024;

ProductionConfigurationValidator.Validate(builder.Configuration, builder.Environment);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("service", "vanta-api"));

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = 5 * 1024 * 1024);
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxRequestBodySize);

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
builder.Services.AddCors(options => options.AddPolicy("frontend", policy =>
{
    if (allowedOrigins.Length > 0)
    {
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
    }
}));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, _) =>
    {
        context.HttpContext.Response.ContentType = "application/problem+json";
        await Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many requests", detail: "Request rate limit exceeded. Try again later.").ExecuteAsync(context.HttpContext);
    };
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        httpContext.Request.Path.StartsWithSegments("/api/webhooks/whatsapp")
            ? RateLimitPartition.GetNoLimiter("whatsapp-webhook")
            : RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Request.Headers.Authorization.ToString() is { Length: > 7 } authorization
                    ? $"token:{authorization[7..]}"
                    : $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 1200, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("auth", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("public", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("webhook", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 2000, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var configuredConnectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:Default configuration.");
var connectionString = PostgresConnectionString.Normalize(configuredConnectionString);

// Resolved lazily (per scope) so WebApplicationFactory-based tests can override
// "Testing:UseInMemoryDatabase" without requiring a real Postgres instance.
builder.Services.AddDbContext<VantaDbContext>((serviceProvider, options) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    if (configuration.GetValue<bool>("Testing:UseInMemoryDatabase"))
    {
        options.UseInMemoryDatabase("vanta-tests");
    }
    else
    {
        options.UseNpgsql(connectionString, npgsql => npgsql.UseVector());
    }
});

builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), tags: ["ready"])
    .AddNpgSql(connectionString, name: "postgres", tags: ["ready"]);

var telemetry = builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("vanta-api"))
    .WithTracing(tracing =>
    {
        tracing.AddSource(VantaTelemetry.ActivitySourceName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();
        if (Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out var tracingEndpoint))
            tracing.AddOtlpExporter(options => options.Endpoint = tracingEndpoint);
        else
            tracing.AddConsoleExporter();
    })
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(VantaTelemetry.MeterName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();
        if (Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out var metricsEndpoint))
            metrics.AddOtlpExporter(options => options.Endpoint = metricsEndpoint);
        else
            metrics.AddConsoleExporter();
    });

builder.Services
    .AddIdentityCore<VantaUser>(options =>
    {
        options.Password.RequiredLength = 8;
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<VantaDbContext>()
    .AddDefaultTokenProviders();

var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services.Configure<JwtOptions>(jwtSection);
builder.Services.AddSingleton<JwtTokenService>();

var jwtOptions = jwtSection.Get<JwtOptions>()
    ?? throw new InvalidOperationException("Missing Jwt configuration section.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddScoped<CommerceEventRecorder>();
builder.Services.AddScoped<AiRevenueAttributionService>();
builder.Services.AddScoped<BackgroundJobService>();
builder.Services.AddScoped<OrderExpirationService>();
builder.Services.AddScoped<ConversationService>();
builder.Services.AddScoped<SalesAgentService>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<IAgentTools, AgentTools>();
builder.Services.AddScoped<AgentToolExecutor>();
builder.Services.AddScoped<KnowledgeIngestionService>();
builder.Services.AddScoped<KnowledgeSearchService>();
builder.Services.Configure<EmbeddingOptions>(builder.Configuration.GetSection("Embeddings"));
builder.Services.AddSingleton<IEmbeddingProvider>(sp =>
{
    var embeddingOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<EmbeddingOptions>>().Value;
    return string.Equals(embeddingOptions.Provider, "gemini", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(embeddingOptions.ApiKey)
        ? new GeminiEmbeddingProvider(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(GeminiEmbeddingProvider)),
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<EmbeddingOptions>>(),
            sp.GetRequiredService<ILogger<GeminiEmbeddingProvider>>())
        : new MockEmbeddingProvider();
});
builder.Services.AddScoped<IVisualProductMatcher, VisualProductMatcher>();
builder.Services.AddScoped<IImageUnderstandingProvider, ImageUnderstandingProvider>();
builder.Services.AddScoped<IAudioTranscriptionProvider, GeminiAudioTranscriptionProvider>();
builder.Services.Configure<AiProviderOptions>(builder.Configuration.GetSection("Ai"));
builder.Services.Configure<CommerceOptions>(builder.Configuration.GetSection("Commerce"));
builder.Services.AddOptions<OrderReservationOptions>()
    .Bind(builder.Configuration.GetSection("OrderReservations"))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<IAgentProvider>(sp =>
    new SmartAgentProvider(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(SmartAgentProvider)),
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AiProviderOptions>>(),
        sp.GetRequiredService<ILogger<SmartAgentProvider>>()));
if (builder.Environment.IsDevelopment() || builder.Environment.EnvironmentName == "Testing")
{
    builder.Services.AddSingleton<IPaymentProvider, MockPaymentProvider>();
}
else if (string.Equals(builder.Configuration["Payment:Provider"], "manual-transfer", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IPaymentProvider, ManualTransferPaymentProvider>();
}
else
{
    throw new InvalidOperationException("Configure Payment:Provider=manual-transfer for the controlled production launch, or register a real payment provider.");
}
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<WhatsAppDeliveryService>();
builder.Services.AddScoped<WhatsAppWebhookProcessor>();
builder.Services.AddSingleton<IWhatsAppProvider, MockWhatsAppProvider>();
builder.Services.AddSingleton<IWhatsAppProvider, MetaWhatsAppProvider>();
builder.Services.AddSingleton<IWhatsAppProvider, LinkedDeviceWhatsAppProvider>();
builder.Services.Configure<WhatsAppProviderOptions>(builder.Configuration.GetSection("WhatsApp"));
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<IMetaMediaClient, MetaMediaClient>();
builder.Services.AddHttpClient<MetaWhatsAppSender>();
builder.Services.AddSingleton<MockWhatsAppSender>();
builder.Services.AddHttpClient<LinkedDeviceWhatsAppSender>();
builder.Services.AddSingleton<IWhatsAppMessageSender, RoutingWhatsAppMessageSender>();
    if (!builder.Configuration.GetValue<bool>("Testing:UseInMemoryDatabase"))
    {
        builder.Services.AddHostedService<BackgroundJobWorker>();
    }

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Database:RunMigrationsAndExit"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<VantaDbContext>().Database.MigrateAsync();
    app.Logger.LogInformation("Database migrations applied successfully.");
    return;
}

if (builder.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<VantaDbContext>().Database.MigrateAsync();
    app.Logger.LogInformation("Database migrations applied successfully during startup.");
}
else if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    if (!configuration.GetValue<bool>("Testing:UseInMemoryDatabase"))
    {
        try
        {
            // Applies pending migrations automatically so a fresh `docker compose up` (or any
            // other freshly provisioned dev Postgres) works without a manual
            // `dotnet ef database update` step. Failures are logged, not fatal: local dev must
            // still be able to start (and report itself unhealthy via /health) when Postgres
            // isn't reachable yet.
            await scope.ServiceProvider.GetRequiredService<VantaDbContext>().Database.MigrateAsync();
        }
        catch (Exception exception)
        {
            app.Logger.LogWarning(exception, "Skipping development database migration/seeding: the database is not reachable yet.");
        }
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseForwardedHeaders();
app.UseMiddleware<CorrelationAndMetricsMiddleware>();
app.UseSerilogRequestLogging();
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var statusCode = exception is JsonException or FormatException or BadHttpRequestException
        ? StatusCodes.Status400BadRequest
        : StatusCodes.Status500InternalServerError;
    var title = statusCode == StatusCodes.Status400BadRequest ? "Invalid request" : "Unexpected server error";
    var detail = statusCode == StatusCodes.Status400BadRequest
        ? "The request payload is invalid."
        : "The server could not complete the request.";
    await Results.Problem(statusCode: statusCode, title: title, detail: detail).ExecuteAsync(context);
}));
app.UseStatusCodePages(async statusContext =>
{
    var response = statusContext.HttpContext.Response;
    if (response.StatusCode < StatusCodes.Status400BadRequest || response.ContentLength.HasValue) return;
    await Results.Problem(
        statusCode: response.StatusCode,
        title: response.StatusCode switch
        {
            StatusCodes.Status401Unauthorized => "Authentication required",
            StatusCodes.Status403Forbidden => "Access denied",
            StatusCodes.Status404NotFound => "Resource not found",
            _ => "Request failed",
        },
        detail: "The request could not be completed.").ExecuteAsync(statusContext.HttpContext);
});
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";

    if (context.Request.ContentLength > maxRequestBodySize)
    {
        await Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "Request too large", detail: "The request body exceeds the maximum allowed size.").ExecuteAsync(context);
        return;
    }

    await next();
});
app.UseHttpsRedirection();
app.UseCors("frontend");
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health", HealthResponseWriter.Options(readyOnly: true));
app.MapHealthChecks("/health/live", HealthResponseWriter.Options(readyOnly: false));
app.MapHealthChecks("/health/ready", HealthResponseWriter.Options(readyOnly: true));

app.MapGroup("/api/auth").RequireRateLimiting("auth").MapAuthEndpoints();
app.MapGroup("/api/tenants").MapTenantEndpoints().RequireAuthorization();
app.MapCatalogModule();
app.MapOrdersModule();
app.MapCustomersModule();
app.MapPaymentsModule();
app.MapWhatsAppModule(
    app.Environment.IsDevelopment() ||
    app.Environment.EnvironmentName == "Testing" ||
    builder.Configuration.GetValue<bool>("Features:EnableMockEndpoints"));
app.MapConversationOperations();
app.MapCartEndpoints();
app.MapPublicCommerceEndpoints();
app.MapKnowledgeModule();
app.MapCommerceEventsModule();

app.Run();

// Exposed so WebApplicationFactory<Program> can bootstrap the app in tests.
public partial class Program;


