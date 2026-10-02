using System.Text;

namespace Vanta.Api.Modules.Tenants;

public static class TenantSlugGenerator
{
    public static string FromName(string name)
    {
        var builder = new StringBuilder(name.Length);
        var previousWasDash = false;

        foreach (var c in name.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
                previousWasDash = false;
            }
            else if (!previousWasDash && builder.Length > 0)
            {
                builder.Append('-');
                previousWasDash = true;
            }
        }

        return builder.ToString().TrimEnd('-');
    }
}
