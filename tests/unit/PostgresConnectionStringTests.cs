using Npgsql;
using Vanta.Api.Data;
using Xunit;

namespace Vanta.Api.UnitTests;

public sealed class PostgresConnectionStringTests
{
    [Fact]
    public void Normalize_converts_postgresql_uri_to_npgsql_connection_string()
    {
        var result = PostgresConnectionString.Normalize(
            "postgresql://postgres.project%2Dref:p%40ss%3Aword@pooler.example.com:5432/postgres?sslmode=require");
        var parsed = new NpgsqlConnectionStringBuilder(result);

        Assert.Equal("pooler.example.com", parsed.Host);
        Assert.Equal(5432, parsed.Port);
        Assert.Equal("postgres", parsed.Database);
        Assert.Equal("postgres.project-ref", parsed.Username);
        Assert.Equal("p@ss:word", parsed.Password);
        Assert.Equal(SslMode.Require, parsed.SslMode);
        Assert.Equal(10, parsed.MaxPoolSize);
    }

    [Fact]
    public void Normalize_preserves_npgsql_key_value_format()
    {
        const string connectionString = "Host=db.internal;Port=5432;Database=vanta;Username=user;Password=secret";

        Assert.Equal(connectionString, PostgresConnectionString.Normalize(connectionString));
    }

    [Theory]
    [InlineData("  'postgresql://user:password@db.example.com/postgres?sslmode=require'  ")]
    [InlineData("\"postgresql://user:password@db.example.com/postgres?sslmode=require\"")]
    public void Normalize_accepts_quoted_postgresql_uri(string connectionString)
    {
        var parsed = new NpgsqlConnectionStringBuilder(PostgresConnectionString.Normalize(connectionString));

        Assert.Equal("db.example.com", parsed.Host);
        Assert.Equal("user", parsed.Username);
    }

    [Fact]
    public void Normalize_rejects_psql_command()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            PostgresConnectionString.Normalize("psql 'postgresql://user:password@db.example.com/postgres'"));

        Assert.Contains("not a psql command", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Server=db.example.com;Database=postgres")]
    [InlineData("ConnectionStrings__Default=Host=db.example.com")]
    [InlineData("Session pooler postgresql://user:password@db.example.com/postgres")]
    public void Normalize_rejects_unknown_format_without_echoing_the_connection_string(string connectionString)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            PostgresConnectionString.Normalize(connectionString));

        Assert.Contains("unsupported format", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("db.example.com", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("postgresql://pooler.example.com/postgres")]
    [InlineData("postgresql://user:password@/postgres")]
    public void Normalize_rejects_incomplete_postgresql_uri(string connectionString)
    {
        Assert.Throws<InvalidOperationException>(() => PostgresConnectionString.Normalize(connectionString));
    }
}
