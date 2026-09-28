namespace Reorder.Core.Models;

/// <summary>
/// A validated, typed supplier row. <see cref="LeadTimeDays"/> is null when the
/// source left it blank; the advisor substitutes <c>AdvisorOptions.DefaultLeadTimeDays</c>.
/// </summary>
public sealed record Supplier(
    string Id,
    string Name,
    int? LeadTimeDays,
    int Moq,
    int PackSize);
