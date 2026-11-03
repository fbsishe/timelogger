namespace TimeLogger.Domain;

public enum SourceType
{
    Tempo = 1,
    FileUpload = 2,
}

public enum ImportStatus
{
    Pending = 0,
    Mapped = 1,
    Submitted = 2,
    Failed = 3,
    Ignored = 4,
    Conflict = 5,
}

public enum SubmissionStatus
{
    Success = 1,
    Failed = 2,
    Retrying = 3,
    Acknowledged = 4,
    Duplicate = 5,

    /// <summary>
    /// A conflict resolved by accepting what is already in Timelog — nothing was written.
    /// Used when the period is closed and the registration was fixed by hand.
    /// </summary>
    KeptExisting = 6,
}

public enum ConflictResolution
{
    UseOurs = 1,
    AddToExisting = 2,
    SetCustom = 3,

    /// <summary>Leave the Timelog registration untouched and close the conflict.</summary>
    KeepExisting = 4,
}

public enum MatchOperator
{
    Equals = 1,
    Contains = 2,
    StartsWith = 3,
    Regex = 4,
}

/// <summary>How a rule combines its conditions: all must match, or any one suffices.</summary>
public enum ConditionCombinator
{
    And = 1,
    Or = 2,
}

public enum AppRole { User = 0, Manager = 1, Admin = 2 }
