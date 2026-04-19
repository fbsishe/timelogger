using TimeLogger.Domain;

namespace TimeLogger.Domain.Tests;

public class EnumValuesTests
{
    [Fact]
    public void ImportStatus_HasExpectedIntegerValues()
    {
        Assert.Equal(0, (int)ImportStatus.Pending);
        Assert.Equal(1, (int)ImportStatus.Mapped);
        Assert.Equal(2, (int)ImportStatus.Submitted);
        Assert.Equal(3, (int)ImportStatus.Failed);
        Assert.Equal(4, (int)ImportStatus.Ignored);
    }

    [Fact]
    public void SubmissionStatus_HasExpectedIntegerValues()
    {
        Assert.Equal(1, (int)SubmissionStatus.Success);
        Assert.Equal(2, (int)SubmissionStatus.Failed);
        Assert.Equal(3, (int)SubmissionStatus.Retrying);
        Assert.Equal(4, (int)SubmissionStatus.Acknowledged);
    }

    [Fact]
    public void MatchOperator_HasExpectedIntegerValues()
    {
        Assert.Equal(1, (int)MatchOperator.Equals);
        Assert.Equal(2, (int)MatchOperator.Contains);
        Assert.Equal(3, (int)MatchOperator.StartsWith);
        Assert.Equal(4, (int)MatchOperator.Regex);
    }

    [Fact]
    public void SourceType_HasExpectedIntegerValues()
    {
        Assert.Equal(1, (int)SourceType.Tempo);
        Assert.Equal(2, (int)SourceType.FileUpload);
    }

    [Fact]
    public void AppRole_HasExpectedIntegerValues()
    {
        Assert.Equal(0, (int)AppRole.User);
        Assert.Equal(1, (int)AppRole.Manager);
        Assert.Equal(2, (int)AppRole.Admin);
    }

    [Fact]
    public void ImportStatus_DefaultValueIsPending()
    {
        Assert.Equal(ImportStatus.Pending, default(ImportStatus));
    }
}
