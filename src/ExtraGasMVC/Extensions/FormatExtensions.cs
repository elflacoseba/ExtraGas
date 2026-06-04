using System.Globalization;

namespace ExtraGasMVC.Extensions;

public static class FormatExtensions
{
    private static readonly CultureInfo ArsCulture = CultureInfo.GetCultureInfo("es-AR");

    public static string ToArs(this decimal value) => value.ToString("C2", ArsCulture);

    public static string ToArs(this decimal? value) => value?.ToString("C2", ArsCulture) ?? "-";

    public static string ToArsRaw(this decimal value) => value.ToString("0.00", ArsCulture);

    public static string ToShortDate(this DateTime value) => value.ToString("dd/MM/yyyy");

    public static string ToShortDate(this DateTime? value) => value?.ToString("dd/MM/yyyy") ?? "-";

    public static string ToShortDateTime(this DateTime value) => value.ToString("dd/MM/yyyy HH:mm");

    public static string ToShortDateTime(this DateTime? value) => value?.ToString("dd/MM/yyyy HH:mm") ?? "-";
}
