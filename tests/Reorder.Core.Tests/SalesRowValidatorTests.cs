using Reorder.Core.Ingestion;

namespace Reorder.Core.Tests;

public sealed class SalesRowValidatorTests
{
    private static readonly DateOnly AsOf = new(2026, 8, 29);
    private static readonly HashSet<string> KnownSkus = new(StringComparer.Ordinal) { "1001", "1002" };

    private static RawSalesRow Row(string date, string sku, string qty, string price, int line = 2) =>
        new(line, $"{date},{sku},{qty},{price}", date, sku, qty, price);

    private static ValidationResult<Reorder.Core.Models.SalesRecord> Validate(params RawSalesRow[] rows) =>
        RowValidator.ValidateSales(rows, KnownSkus, AsOf);

    [Fact]
    public void Valid_row_is_accepted_and_typed()
    {
        var result = Validate(Row("2026-06-01", "1001", "5", "233.84"));

        Assert.Empty(result.Rejected);
        var record = Assert.Single(result.Accepted);
        Assert.Equal(new DateOnly(2026, 6, 1), record.Date);
        Assert.Equal("1001", record.Sku);
        Assert.Equal(5, record.QtySold);
        Assert.Equal(233.84m, record.UnitPrice);
    }

    [Fact]
    public void Zero_quantity_is_valid()
    {
        var result = Validate(Row("2026-06-01", "1001", "0", "233.84"));

        Assert.Empty(result.Rejected);
        Assert.Equal(0, Assert.Single(result.Accepted).QtySold);
    }

    [Fact]
    public void Date_equal_to_as_of_is_valid()
    {
        var result = Validate(Row("2026-08-29", "1001", "3", "10.00"));

        Assert.Empty(result.Rejected);
        Assert.Single(result.Accepted);
    }

    [Theory]
    [InlineData("", "1001", "5", "233.84")]   // missing date
    [InlineData("2026-06-01", "", "5", "233.84")]   // missing sku
    [InlineData("2026-06-01", "1001", "", "233.84")]   // missing qty
    [InlineData("2026-06-01", "1001", "5", "")]   // missing price
    public void Missing_field_is_rejected(string date, string sku, string qty, string price)
    {
        var result = Validate(Row(date, sku, qty, price));

        AssertRejected(result, RejectReason.MissingField);
    }

    [Theory]
    [InlineData("22/06/2026")]   // valid-looking in another culture — must NOT be reinterpreted
    [InlineData("2026-13-09")]   // impossible month
    [InlineData("Jun 4 2026")]   // month name
    [InlineData("N/A")]
    public void Malformed_date_is_bad_type(string date)
    {
        var result = Validate(Row(date, "1001", "5", "10.00"));

        AssertRejected(result, RejectReason.BadType);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("twelve")]
    [InlineData("3.5")]     // not an integer
    [InlineData("1,000")]   // thousands separator disallowed
    public void Bad_quantity_is_bad_type(string qty)
    {
        var result = Validate(Row("2026-06-01", "1001", qty, "10.00"));

        AssertRejected(result, RejectReason.BadType);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("abc")]
    public void Bad_or_non_positive_price_is_bad_type(string price)
    {
        var result = Validate(Row("2026-06-01", "1001", "5", price));

        AssertRejected(result, RejectReason.BadType);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("-5")]
    public void Negative_quantity_is_rejected(string qty)
    {
        var result = Validate(Row("2026-06-01", "1001", qty, "10.00"));

        AssertRejected(result, RejectReason.NegativeQty);
    }

    [Fact]
    public void Future_date_is_rejected()
    {
        var result = Validate(Row("2026-09-08", "1001", "3", "10.00"));

        AssertRejected(result, RejectReason.FutureDate);
    }

    [Fact]
    public void Unknown_sku_is_rejected()
    {
        var result = Validate(Row("2026-06-01", "9025", "1", "10.00"));

        AssertRejected(result, RejectReason.UnknownSku);
    }

    [Fact]
    public void Duplicate_key_keeps_the_first_and_rejects_the_later()
    {
        var result = Validate(
            Row("2026-06-01", "1001", "5", "10.00", line: 2),
            Row("2026-06-01", "1001", "6", "10.00", line: 3));

        var accepted = Assert.Single(result.Accepted);
        Assert.Equal(5, accepted.QtySold);   // first kept
        var reject = Assert.Single(result.Rejected);
        Assert.Equal(RejectReason.DuplicateKey, reject.Reason);
        Assert.Equal(3, reject.Line);        // later rejected
    }

    [Fact]
    public void Field_checks_win_over_duplicate_detection()
    {
        // A negative-qty copy of a clean row shares the (date, sku) key, but must be
        // classified NEGATIVE_QTY — not DUPLICATE_KEY — so counts match the generator's intent.
        var result = Validate(
            Row("2026-06-01", "1001", "5", "10.00", line: 2),
            Row("2026-06-01", "1001", "-5", "10.00", line: 3));

        Assert.Single(result.Accepted);
        Assert.Equal(RejectReason.NegativeQty, Assert.Single(result.Rejected).Reason);
    }

    [Fact]
    public void Missing_field_wins_over_other_problems()
    {
        // Empty date AND a bad qty: MISSING_FIELD takes precedence.
        var result = Validate(Row("", "1001", "abc", "10.00"));

        AssertRejected(result, RejectReason.MissingField);
    }

    private static void AssertRejected(
        ValidationResult<Reorder.Core.Models.SalesRecord> result, RejectReason expected)
    {
        Assert.Empty(result.Accepted);
        Assert.Equal(expected, Assert.Single(result.Rejected).Reason);
    }
}
