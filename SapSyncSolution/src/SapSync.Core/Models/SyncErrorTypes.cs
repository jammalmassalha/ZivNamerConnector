namespace SapSync.Core.Models;

/// <summary>Values used for <see cref="SyncResult.ErrorType"/>.</summary>
public static class SyncErrorTypes
{
    /// <summary>The request is invalid (unknown table/columns, missing keys, ...).</summary>
    public const string Validation = "Validation";

    /// <summary>Reading from SAP failed (logon, communication, ABAP exception, ...).</summary>
    public const string SapRead = "SapRead";

    /// <summary>Writing to SQL Server failed.</summary>
    public const string SqlWrite = "SqlWrite";
}
