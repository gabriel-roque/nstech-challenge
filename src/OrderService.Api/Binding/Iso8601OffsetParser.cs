using System.Globalization;

namespace OrderService.Api.Binding;

internal static class Iso8601OffsetParser
{
    public static bool TryParseOptional(string? text, out DateTimeOffset? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed) ||
            !(text.EndsWith('Z') || HasExplicitOffset(text)))
        {
            return false;
        }

        value = parsed.ToUniversalTime();
        return true;
    }

    private static bool HasExplicitOffset(string text)
    {
        var timeSeparator = text.IndexOf('T', StringComparison.OrdinalIgnoreCase);
        return timeSeparator >= 0 &&
               (text.LastIndexOf('+') > timeSeparator || text.LastIndexOf('-') > timeSeparator);
    }
}
