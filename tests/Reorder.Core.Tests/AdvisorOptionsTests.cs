using System.ComponentModel.DataAnnotations;

namespace Reorder.Core.Tests;

public sealed class AdvisorOptionsTests
{
    [Fact]
    public void Defaults_match_the_implementation_plan()
    {
        var options = new AdvisorOptions();

        Assert.Equal(28, options.WindowDays);
        Assert.Equal(1.65, options.ServiceLevelZ);
        Assert.Equal(30, options.CoverDays);
        Assert.Equal(90, options.OverstockDays);
        Assert.Equal(60, options.DeadStockDays);
        Assert.Equal(7, options.DefaultLeadTimeDays);
        Assert.Equal(14, options.LowConfidenceHistoryDays);
        Assert.Equal(0.20, options.MaxRejectRatio);
    }

    [Fact]
    public void Defaults_pass_validation()
    {
        Assert.Empty(Validate(new AdvisorOptions()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(400)]
    public void WindowDays_out_of_range_fails_validation(int windowDays)
    {
        var errors = Validate(new AdvisorOptions { WindowDays = windowDays });

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(AdvisorOptions.WindowDays)));
    }

    [Fact]
    public void Reject_ratio_above_one_fails_validation()
    {
        var errors = Validate(new AdvisorOptions { MaxRejectRatio = 1.5 });

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(AdvisorOptions.MaxRejectRatio)));
    }

    private static List<ValidationResult> Validate(AdvisorOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }
}
