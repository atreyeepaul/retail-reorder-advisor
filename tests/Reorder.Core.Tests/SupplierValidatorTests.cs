using Reorder.Core.Ingestion;

namespace Reorder.Core.Tests;

public sealed class SupplierValidatorTests
{
    private static RawSupplierRow Row(
        string id, string name, string leadTime, string moq, string packSize, int line = 2) =>
        new(line, $"{id},{name},{leadTime},{moq},{packSize}", id, name, leadTime, moq, packSize);

    private static ValidationResult<Reorder.Core.Models.Supplier> Validate(params RawSupplierRow[] rows) =>
        RowValidator.ValidateSuppliers(rows);

    [Fact]
    public void Valid_row_is_accepted_and_typed()
    {
        var result = Validate(Row("SUP-01", "Annapurna Foods", "5", "12", "12"));

        Assert.Empty(result.Rejected);
        var supplier = Assert.Single(result.Accepted);
        Assert.Equal("SUP-01", supplier.Id);
        Assert.Equal(5, supplier.LeadTimeDays);
        Assert.Equal(12, supplier.Moq);
        Assert.Equal(12, supplier.PackSize);
    }

    [Fact]
    public void Blank_lead_time_is_accepted_as_null()
    {
        // SUP-07 Polar Frozen in the sample data: "SUP-07,Polar Frozen,,6,6"
        var result = Validate(Row("SUP-07", "Polar Frozen", "", "6", "6"));

        Assert.Empty(result.Rejected);
        Assert.Null(Assert.Single(result.Accepted).LeadTimeDays);
    }

    [Fact]
    public void Blank_moq_and_pack_size_default_to_one()
    {
        var result = Validate(Row("SUP-01", "Name", "5", "", ""));

        Assert.Empty(result.Rejected);
        var supplier = Assert.Single(result.Accepted);
        Assert.Equal(1, supplier.Moq);
        Assert.Equal(1, supplier.PackSize);
    }

    [Theory]
    [InlineData("", "Name")]   // missing id
    [InlineData("SUP-01", "")]   // missing name
    public void Missing_field_is_rejected(string id, string name)
    {
        var result = Validate(Row(id, name, "5", "12", "12"));

        AssertRejected(result, RejectReason.MissingField);
    }

    [Theory]
    [InlineData("abc", "12", "12")]   // lead time not int
    [InlineData("0", "12", "12")]     // lead time below range
    [InlineData("200", "12", "12")]   // lead time above range
    [InlineData("5", "0", "12")]      // moq below 1
    [InlineData("5", "12", "abc")]    // pack size not int
    public void Bad_type_is_rejected(string leadTime, string moq, string packSize)
    {
        var result = Validate(Row("SUP-01", "Name", leadTime, moq, packSize));

        AssertRejected(result, RejectReason.BadType);
    }

    [Fact]
    public void Duplicate_supplier_id_keeps_the_first()
    {
        var result = Validate(
            Row("SUP-01", "First", "5", "12", "12", line: 2),
            Row("SUP-01", "Second", "5", "12", "12", line: 3));

        Assert.Equal("First", Assert.Single(result.Accepted).Name);
        Assert.Equal(RejectReason.DuplicateKey, Assert.Single(result.Rejected).Reason);
    }

    private static void AssertRejected(
        ValidationResult<Reorder.Core.Models.Supplier> result, RejectReason expected)
    {
        Assert.Empty(result.Accepted);
        Assert.Equal(expected, Assert.Single(result.Rejected).Reason);
    }
}
