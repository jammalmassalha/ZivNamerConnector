namespace SapSync.Infrastructure.Sap;

/// <summary>A row of the <c>FIELDS</c> table returned by <c>RFC_READ_TABLE</c>.</summary>
internal sealed record SapFieldMetadata(string Name, string AbapType, int Length, string Description);
