using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using ParcelNumberGenerator.Notifications.Data;

namespace ParcelNumberGenerator.Notifications.Domain;

/// <summary>
/// P11 — anti-corruption at the edge. Parcel numbers reach this service in whatever
/// dialect the upstream that raised the event happens to speak: the canonical PNG form,
/// a bare barcode scan with no prefix, the legacy WMS <c>WMS/</c> form, or any of those
/// with the separators a human typed. They are normalized here, once, and nothing
/// downstream — service, repository, schema or API response — knows there was ever more
/// than one dialect.
/// </summary>
/// <remarks>
/// This type validates and normalizes; it deliberately does not <em>mint</em> parcel
/// numbers. Allocating a new number is the Parcel Number Generator service's bounded
/// context (P3), and a second service that can also mint one is a duplicate-key incident
/// waiting for its first busy morning. See <c>docs/decisions/0003-parcel-number-format.md</c>.
/// </remarks>
public readonly record struct ParcelNumber
{
    private const string CanonicalPrefix = "PNG";
    private const string LegacyWmsPrefix = "WMS";
    private const int PayloadLength = 8;

    private ParcelNumber(string canonical) => Canonical = canonical;

    /// <summary>The canonical form, <c>PNG-12345678-5</c>. Always exactly this shape.</summary>
    public string Canonical { get; }

    public override string ToString() => Canonical;

    /// <summary>
    /// Normalizes any accepted dialect to canonical form. Returns false rather than
    /// throwing: a bad parcel number on an inbound event is a 400, not an exception, and
    /// the caller decides which.
    /// </summary>
    public static bool TryParse(string? candidate, out ParcelNumber parcelNumber)
    {
        parcelNumber = default;

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var compacted = Compact(candidate);

        if (compacted.Length is 0)
        {
            return false;
        }

        compacted = StripKnownPrefix(compacted);

        if (!IsAllDigits(compacted))
        {
            return false;
        }

        string payload;

        switch (compacted.Length)
        {
            case PayloadLength:
                // A scan with no check digit. Compute it rather than reject: the check
                // digit exists to catch transcription errors, and there was no
                // transcription.
                payload = compacted;
                break;

            case PayloadLength + 1:
                payload = compacted[..PayloadLength];
                var supplied = compacted[PayloadLength] - '0';

                if (supplied != CheckDigit(payload))
                {
                    return false;
                }

                break;

            default:
                return false;
        }

        parcelNumber = new ParcelNumber(
            string.Create(
                ParcelNumberLimits.CanonicalLength,
                (payload, check: CheckDigit(payload)),
                static (destination, state) =>
                {
                    CanonicalPrefix.AsSpan().CopyTo(destination);
                    destination[3] = '-';
                    state.payload.AsSpan().CopyTo(destination[4..]);
                    destination[12] = '-';
                    destination[13] = (char)('0' + state.check);
                }));

        return true;
    }

    /// <summary>
    /// Normalizes an optional parcel number. Distinguishes "absent" (valid — not every
    /// notification is parcel-scoped) from "present but unparseable" (invalid).
    /// </summary>
    public static bool TryParseOptional(string? candidate, out string? canonical)
    {
        canonical = null;

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return true;
        }

        if (!TryParse(candidate, out var parcelNumber))
        {
            return false;
        }

        canonical = parcelNumber.Canonical;
        return true;
    }

    public static ParcelNumber Parse(string candidate) =>
        TryParse(candidate, out var parcelNumber)
            ? parcelNumber
            : throw new FormatException(
                $"'{candidate}' is not a recognized parcel number in any accepted dialect.");

    /// <summary>
    /// Uppercases and drops the separators a scanner, a label or a human might introduce.
    /// Everything else is left alone so it fails the digit check rather than being
    /// silently mangled into something valid.
    /// </summary>
    private static string Compact(string candidate)
    {
        Span<char> buffer = stackalloc char[candidate.Length];
        var length = 0;

        foreach (var character in candidate)
        {
            if (character is ' ' or '-' or '/' or '.' or '_' or '\t')
            {
                continue;
            }

            buffer[length++] = char.ToUpperInvariant(character);
        }

        return new string(buffer[..length]);
    }

    private static string StripKnownPrefix(string compacted) =>
        compacted.StartsWith(CanonicalPrefix, StringComparison.Ordinal) ? compacted[CanonicalPrefix.Length..]
        : compacted.StartsWith(LegacyWmsPrefix, StringComparison.Ordinal) ? compacted[LegacyWmsPrefix.Length..]
        : compacted;

    private static bool IsAllDigits(string value)
    {
        foreach (var character in value)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return value.Length is not 0;
    }

    /// <summary>
    /// Luhn check digit over the eight-digit payload. Luhn catches every single-digit
    /// error and almost every adjacent transposition, which between them are what a
    /// mis-keyed parcel number actually is.
    /// </summary>
    internal static int CheckDigit(string payload)
    {
        var sum = 0;
        var doubling = true;

        for (var index = payload.Length - 1; index >= 0; index--)
        {
            var digit = payload[index] - '0';

            if (doubling)
            {
                digit *= 2;

                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            doubling = !doubling;
        }

        return (10 - (sum % 10)) % 10;
    }

    /// <summary>Renders the canonical form for a payload, for tests and fixtures.</summary>
    internal static string Canonicalize(int payload) =>
        Parse(payload.ToString("D8", CultureInfo.InvariantCulture)).Canonical;

    [SuppressMessage(
        "Design",
        "CA1065:Do not raise exceptions in unexpected locations",
        Justification = "Explicit conversion; TryParse is the non-throwing path.")]
    public static explicit operator ParcelNumber(string candidate) => Parse(candidate);
}
