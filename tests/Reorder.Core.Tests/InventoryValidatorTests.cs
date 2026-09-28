using Reorder.Core.Ingestion;

namespace Reorder.Core.Tests;

public sealed class InventoryValidatorTests
{
    private static readonly HashSet<string> SupplierIds = new(StringComparer.Ordinal) { "SUP-01", "SUP-02" };

    private static RawInventoryRow Row(
        string sku, string name, string category, string onHand, string unitCost, string supplierId, int line = 2) =>
        new(line, $"{sku},{name},{category},{onHand},{unitCost},{supplierId}",
            sku, name, category, onHand, unitCost, supplierId);

    private static ValidationResult<Reorder.Core.Models.InventoryItem> Validate(params RawInventoryRow[] rows) =>
        RowValidator.ValidateInventory(rows, SupplierIds);

    [Fact]
    public void Valid_row_is_accepted_and_typed()
    {
        var result = Validate(Row("1001", "Toilet Cleaner 500ml", "Household", "35", "185.22", "SUP-01"));

        Assert.Empty(result.Rejected);
        var item = Assert.Single(result.Accepted);
        Assert.Equal("1001", item.Sku);
        Assert.Equal("Household", item.Category);
        Assert.Equal(35, item.OnHand);
        Assert.Equal(185.22m, item.UnitCost);
        Assert.Equal("SUP-01", item.SupplierId);
    }

    [Fact]
    public void Blank_category_is_allowed_and_becomes_null()
    {
        var result = Validate(Row("1001", "Widget", "", "5", "10.00", "SUP-01"));

        Assert.Empty(result.Rejected);
        Assert.Null(Assert.Single(result.Accepted).Category);
    }

    [Theory]
    [InlineData("", "Name", "5", "10.00", "SUP-01")]      // missing sku
    [InlineData("1001", "", "5", "10.00", "SUP-01")]      // missing name
    [InlineData("1001", "Name", "", "10.00", "SUP-01")]   // missing on_hand
    [InlineData("1001", "Name", "5", "", "SUP-01")]       // missing unit_cost
    [InlineData("1001", "Name", "5", "10.00", "")]        // missing supplier_id
    public void Missing_field_is_rejected(string sku, string name, string onHand, string unitCost, string supplierId)
    {
        var result = Validate(Row(sku, name, "Cat", onHand, unitCost, supplierId));

        AssertRejected(result, RejectReason.MissingField);
    }

    [Theory]
    [InlineData("abc", "10.00")]   // on_hand not int
    [InlineData("-3", "10.00")]    // on_hand negative
    [InlineData("5", "abc")]       // unit_cost not decimal
    [InlineData("5", "0")]         // unit_cost not positive
    public void Bad_type_is_rejected(string onHand, string unitCost)
    {
        var result = Validate(Row("1001", "Name", "Cat", onHand, unitCost, "SUP-01"));

        AssertRejected(result, RejectReason.BadType);
    }

    [Fact]
    public void Unknown_supplier_is_rejected()
    {
        var result = Validate(Row("1001", "Name", "Cat", "5", "10.00", "SUP-99"));

        AssertRejected(result, RejectReason.UnknownSupplier);
    }

    [Fact]
    public void Duplicate_sku_keeps_the_first()
    {
        var result = Validate(
            Row("1001", "First", "Cat", "5", "10.00", "SUP-01", line: 2),
            Row("1001", "Second", "Cat", "6", "10.00", "SUP-01", line: 3));

        Assert.Equal("First", Assert.Single(result.Accepted).Name);
        var reject = Assert.Single(result.Rejected);
        Assert.Equal(RejectReason.DuplicateKey, reject.Reason);
        Assert.Equal(3, reject.Line);
    }

    private static void AssertRejected(
        ValidationResult<Reorder.Core.Models.InventoryItem> result, RejectReason expected)
    {
        Assert.Empty(result.Accepted);
        Assert.Equal(expected, Assert.Single(result.Rejected).Reason);
    }
}
