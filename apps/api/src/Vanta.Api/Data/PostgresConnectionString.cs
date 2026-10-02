using Npgsql;

namespace Vanta.Api.Data;

public static class PostgresConnectionString
{
    public static string Normalize(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            value = value[1..^1].Trim();
        }

        if (value.StartsWith("psql ", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Default must contain only the PostgreSQL connection string, not a psql command.");
        }

        if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            if (value.StartsWith("Host=", StringComparison.OrdinalIgnoreCase)) return value;

            var prefixLength = Math.Min(value.IndexOfAny([' ', ':', '=']) is var separator && separator >= 0
                ? separator
                : value.Length, 24);
            var safePrefix = value[..prefixLength];
            throw new InvalidOperationException(
                $"ConnectionStrings:Default has an unsupported format (prefix: '{safePrefix}'). Use only a postgresql:// URI or an Npgsql string beginning with Host=.");
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            string.IsNullOrWhiteSpace(uri.AbsolutePath.Trim('/')))
        {
            throw new InvalidOperationException("ConnectionStrings:Default is not a valid PostgreSQL URI.");
        }

        var userInfo = uri.UserInfo.Split(':', 2);
        if (userInfo.Length != 2)
        {
            throw new InvalidOperationException("The PostgreSQL URI must include a username and password.");
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/')),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = Uri.UnescapeDataString(userInfo[1]),
            SslMode = ReadSslMode(uri.Query),
            MaxPoolSize = 10,
        };

        return builder.ConnectionString;
    }

    private static SslMode ReadSslMode(string query)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase) &&
                Enum.TryParse<SslMode>(Uri.UnescapeDataString(parts[1]), ignoreCase: true, out var sslMode))
            {
                return sslMode;
            }
        }

        return SslMode.Require;
    }
}
