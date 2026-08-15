using ParcelNumberGenerator.Notifications.Domain;

namespace ParcelNumberGenerator.Notifications.Tests;

/// <summary>
/// P11's test: whatever dialect an upstream speaks, exactly one form reaches the
/// database. Every expected value here is the canonical form with a Luhn check digit
/// computed by hand, not by calling the code under test.
/// </summary>
public sealed class ParcelNumberTests
{
    [Theory]
    // Already canonical.
    [InlineData("PNG-12345678-2", "PNG-12345678-2")]
    // A bare barcode scan: eight digits, no prefix, no check digit. The check digit
    // guards against transcription errors, and a scan was not transcribed — so it is
    // computed rather than demanded.
    [InlineData("12345678", "PNG-12345678-2")]
    // Nine digits: the ninth is the check digit and is verified.
    [InlineData("123456782", "PNG-12345678-2")]
    // The legacy WMS dialect the warehouse's old system emits.
    [InlineData("WMS/12345678", "PNG-12345678-2")]
    [InlineData("WMS/123456782", "PNG-12345678-2")]
    // Human typing: lowercase, spaces, mixed separators.
    [InlineData("png 1234 5678 2", "PNG-12345678-2")]
    [InlineData("png-12345678-2", "PNG-12345678-2")]
    [InlineData("  PNG12345678  ", "PNG-12345678-2")]
    [InlineData("PNG.12345678.2", "PNG-12345678-2")]
    // Leading zeros survive; they are part of the number, not formatting.
    [InlineData("00000001", "PNG-00000001-8")]
    [InlineData("99999999", "PNG-99999999-8")]
    public void TryParse_normalizes_every_accepted_dialect_to_one_canonical_form(
        string dialect,
        string expected)
    {
        Assert.True(ParcelNumber.TryParse(dialect, out var parcelNumber));
        Assert.Equal(expected, parcelNumber.Canonical);
    }

    [Theory]
    // Wrong check digit: 3 where the payload demands 2. This is the single-digit error
    // Luhn exists to catch.
    [InlineData("PNG-12345678-3")]
    [InlineData("123456783")]
    // Adjacent transposition in the payload changes the required check digit, so the
    // supplied one no longer matches.
    [InlineData("213456782")]
    // Too short, too long.
    [InlineData("1234567")]
    [InlineData("1234567890")]
    // Not digits.
    [InlineData("PNG-1234567A-2")]
    [InlineData("not-a-parcel")]
    // An unknown prefix is not silently stripped — it fails the digit check.
    [InlineData("DHL/12345678")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryParse_rejects_what_is_not_a_parcel_number(string? candidate)
    {
        Assert.False(ParcelNumber.TryParse(candidate, out _));
    }

    [Fact]
    public void Canonical_form_is_always_the_length_the_schema_reserves()
    {
        Assert.True(ParcelNumber.TryParse("12345678", out var parcelNumber));

        Assert.Equal(
            ParcelNumberGenerator.Notifications.Data.ParcelNumberLimits.CanonicalLength,
            parcelNumber.Canonical.Length);
    }

    [Fact]
    public void Parsing_is_idempotent()
    {
        Assert.True(ParcelNumber.TryParse("wms/12345678", out var first));
        Assert.True(ParcelNumber.TryParse(first.Canonical, out var second));

        Assert.Equal(first.Canonical, second.Canonical);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParseOptional_treats_absence_as_valid(string? absent)
    {
        Assert.True(ParcelNumber.TryParseOptional(absent, out var canonical));
        Assert.Null(canonical);
    }

    [Fact]
    public void TryParseOptional_still_rejects_a_present_but_unparseable_value()
    {
        Assert.False(ParcelNumber.TryParseOptional("DHL/999", out _));
    }

    [Fact]
    public void Parse_throws_only_on_the_explicit_path()
    {
        Assert.Throws<FormatException>(() => ParcelNumber.Parse("nonsense"));
    }
}
