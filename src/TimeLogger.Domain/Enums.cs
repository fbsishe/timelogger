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
}

public enum SubmissionStatus
{
    Success = 1,
    Failed = 2,
    Retrying = 3,
    Acknowledged = 4,
}

public enum MatchOperator
{
    Equals = 1,
    Contains = 2,
    StartsWith = 3,
    Regex = 4,
}

public enum AppRole { User = 0, Manager = 1, Admin = 2 }
