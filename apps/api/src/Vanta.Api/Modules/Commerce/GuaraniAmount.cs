using System.Globalization;

namespace Vanta.Api.Modules.Commerce;

public static class GuaraniAmount
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("es-PY");

    public static string Format(decimal amount) => amount.ToString("N0", Culture);
}
