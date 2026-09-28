namespace Reorder.Core.Ingestion;

/// <summary>One rejected row: which file, the 1-based line, why, and the raw text.</summary>
public sealed record Reject(string File, int Line, RejectReason Reason, string Raw);
