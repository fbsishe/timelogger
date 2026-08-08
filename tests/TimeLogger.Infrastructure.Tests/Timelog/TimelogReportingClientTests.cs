using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TimeLogger.Infrastructure.Timelog;

namespace TimeLogger.Infrastructure.Tests.Timelog;

public class TimelogReportingClientTests
{
    private const string Ns = "http://www.timelog.com/XML/Schema/tlp/v4_4";

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(respond(request));
        }
    }

    private static TimelogReportingClient CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        TimelogReportingOptions? options = null)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
               .Returns(new HttpClient(new StubHandler(respond)));

        return new TimelogReportingClient(
            factory.Object,
            Options.Create(options ?? new TimelogReportingOptions { SiteCode = "s", ApiId = "i", ApiPassword = "p" }),
            Options.Create(new TimelogOptions { BaseUrl = "https://app2.timelog.test/acct/api", ApiKey = "k" }),
            NullLogger<TimelogReportingClient>.Instance);
    }

    private static HttpResponseMessage Xml(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "text/xml"),
    };

    [Fact]
    public async Task ParsesWorkUnits_AndDerivesServiceUrlFromBaseUrl()
    {
        var xml = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <tlp:WorkUnits Page="1" PageSize="500" TotalPages="1" TotalRecords="2" xmlns:tlp="{Ns}">
              <tlp:WorkUnit ID="90285">
                <tlp:TimeRegistrationGuid>29ED549B-5DBC-409D-81D0-43CF0E05AA62</tlp:TimeRegistrationGuid>
                <tlp:UserID>25</tlp:UserID>
                <tlp:TaskID>640</tlp:TaskID>
                <tlp:TaskName>Seniora utvecklare</tlp:TaskName>
                <tlp:ProjectName>ADVA / Advania-Flow</tlp:ProjectName>
                <tlp:Date>2026-08-07T00:00:00.0000000</tlp:Date>
                <tlp:Note>AFL-28 work</tlp:Note>
                <tlp:RegHours>5.0000</tlp:RegHours>
                <tlp:ApprovedStatus>10</tlp:ApprovedStatus>
                <tlp:InvoiceStatus>0</tlp:InvoiceStatus>
                <tlp:CreatedAt>2026-08-08T09:00:00.0000000</tlp:CreatedAt>
                <tlp:LastModifiedAt>2026-08-08T09:30:00.0000000</tlp:LastModifiedAt>
              </tlp:WorkUnit>
              <tlp:WorkUnit ID="90286">
                <tlp:TimeRegistrationGuid>AAAAAAAA-0000-0000-0000-000000000001</tlp:TimeRegistrationGuid>
                <tlp:UserID>8</tlp:UserID>
                <tlp:TaskID>543</tlp:TaskID>
                <tlp:RegHours>1.5000</tlp:RegHours>
                <tlp:ApprovedStatus>0</tlp:ApprovedStatus>
                <tlp:InvoiceStatus>1</tlp:InvoiceStatus>
              </tlp:WorkUnit>
            </tlp:WorkUnits>
            """;

        string? requestedUrl = null;
        var client = CreateClient(req =>
        {
            requestedUrl = req.RequestUri!.ToString();
            return Xml(xml);
        });

        var units = await client.GetWorkUnitsAsync(new DateOnly(2026, 8, 7), new DateOnly(2026, 8, 7));

        Assert.Equal("https://app2.timelog.test/acct/service.asmx/GetWorkUnitsRawPaged", requestedUrl);
        Assert.Equal(2, units.Count);

        var robert = units[0];
        Assert.Equal("29ED549B-5DBC-409D-81D0-43CF0E05AA62", robert.TimeRegistrationGuid);
        Assert.Equal(25, robert.UserId);
        Assert.Equal(640, robert.TaskId);
        Assert.Equal(5.0, robert.Hours);
        Assert.Equal(10, robert.ApprovedStatus);
        Assert.False(robert.Invoiced);
        Assert.Equal(new DateOnly(2026, 8, 7), robert.Date);
        Assert.NotNull(robert.Created);

        Assert.True(units[1].Invoiced);
        Assert.Equal(1.5, units[1].Hours);
    }

    [Fact]
    public async Task ErrorResponse_Throws()
    {
        var xml = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <tlp:Errors xmlns:tlp="{Ns}">
              <tlp:Error>Access denied. Check siteCode, apiID, apiPassword and SSL settings</tlp:Error>
            </tlp:Errors>
            """;

        var client = CreateClient(_ => Xml(xml));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetWorkUnitsAsync(new DateOnly(2026, 8, 7), new DateOnly(2026, 8, 7)));
        Assert.Contains("Access denied", ex.Message);
    }

    [Fact]
    public async Task NotConfigured_Throws()
    {
        var client = CreateClient(_ => Xml("<x/>"), new TimelogReportingOptions());

        Assert.False(client.IsConfigured);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetWorkUnitsAsync(new DateOnly(2026, 8, 7), new DateOnly(2026, 8, 7)));
    }

    [Fact]
    public async Task FollowsPagination()
    {
        string Page(int page, int total, string guid) => $"""
            <?xml version="1.0" encoding="utf-8"?>
            <tlp:WorkUnits Page="{page}" PageSize="500" TotalPages="{total}" TotalRecords="2" xmlns:tlp="{Ns}">
              <tlp:WorkUnit ID="{page}">
                <tlp:TimeRegistrationGuid>{guid}</tlp:TimeRegistrationGuid>
                <tlp:UserID>1</tlp:UserID><tlp:TaskID>1</tlp:TaskID><tlp:RegHours>1</tlp:RegHours>
                <tlp:ApprovedStatus>0</tlp:ApprovedStatus><tlp:InvoiceStatus>0</tlp:InvoiceStatus>
                <tlp:Date>2026-08-07T00:00:00</tlp:Date>
              </tlp:WorkUnit>
            </tlp:WorkUnits>
            """;

        var call = 0;
        var client = CreateClient(_ => Xml(Page(++call, 2, $"guid-{call}")));

        var units = await client.GetWorkUnitsAsync(new DateOnly(2026, 8, 7), new DateOnly(2026, 8, 7));

        Assert.Equal(2, call);
        Assert.Equal(["guid-1", "guid-2"], units.Select(u => u.TimeRegistrationGuid));
    }
}
