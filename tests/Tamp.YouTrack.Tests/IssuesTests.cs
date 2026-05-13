using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tamp;
using Tamp.YouTrack;
using Xunit;

namespace Tamp.YouTrack.Tests;

public sealed class IssuesTests
{
    private static (YouTrackClient yt, RecordingSpy spy) Fake(string responseBody = "{\"idReadable\":\"TAM-999\",\"summary\":\"new\"}")
    {
        var spy = new RecordingSpy { ResponseBody = responseBody };
        var yt = new YouTrackClient(
            "https://workspace.youtrack.cloud/",
            new Secret("tok", "perma-token-xyz"),
            http: new HttpClient(spy));
        return (yt, spy);
    }

    // ─── Construction / auth ──────────────────────────────────────────────

    [Fact]
    public void Workspace_Url_Is_Normalized()
    {
        var (yt, _) = Fake();
        Assert.Equal(new Uri("https://workspace.youtrack.cloud/"), yt.WorkspaceUrl);
    }

    [Fact]
    public async Task Bearer_Auth_Header_Is_Set()
    {
        var (yt, spy) = Fake();
        await yt.Issues.GetByIdAsync("TAM-1");
        var req = spy.Requests.Single();
        Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
        Assert.Equal("perma-token-xyz", req.Headers.Authorization.Parameter);
    }

    [Fact]
    public void Constructor_Rejects_Null_WorkspaceUrl()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new YouTrackClient(null!, new Secret("t", "x")));
    }

    // ─── Create ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_Posts_To_Api_Issues_With_Body()
    {
        var (yt, spy) = Fake();
        await yt.Issues.CreateAsync(projectId: "0-2", summary: "Filed by Tamp", description: "details here");
        var req = spy.Requests.Single();
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("/api/issues", req.RequestUri!.AbsolutePath);
        Assert.Contains("fields=", req.RequestUri.Query);

        var bodyJson = JsonDocument.Parse(spy.RequestBodies.Single()).RootElement;
        Assert.Equal("0-2", bodyJson.GetProperty("project").GetProperty("id").GetString());
        Assert.Equal("Filed by Tamp", bodyJson.GetProperty("summary").GetString());
        Assert.Equal("details here", bodyJson.GetProperty("description").GetString());
    }

    [Fact]
    public async Task Create_Requires_ProjectId()
    {
        var (yt, _) = Fake();
        await Assert.ThrowsAsync<ArgumentException>(() => yt.Issues.CreateAsync("", "summary"));
    }

    [Fact]
    public async Task Create_Requires_Summary()
    {
        var (yt, _) = Fake();
        await Assert.ThrowsAsync<ArgumentException>(() => yt.Issues.CreateAsync("0-2", ""));
    }

    [Fact]
    public async Task Create_With_Description_Null_Omits_Description_From_Body_Or_Sends_Null()
    {
        var (yt, spy) = Fake();
        await yt.Issues.CreateAsync(projectId: "0-2", summary: "no desc");
        var bodyJson = JsonDocument.Parse(spy.RequestBodies.Single()).RootElement;
        // null property is acceptable; what matters is that creating without a description works.
        Assert.True(!bodyJson.TryGetProperty("description", out var d) || d.ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task Create_With_State_Custom_Field()
    {
        var (yt, spy) = Fake();
        await yt.Issues.CreateAsync(
            projectId: "0-2", summary: "state test",
            customFields: new[] { CustomFieldValue.StateByName("Open") });

        var bodyJson = JsonDocument.Parse(spy.RequestBodies.Single()).RootElement;
        var fields = bodyJson.GetProperty("customFields").EnumerateArray().Single();
        Assert.Equal("State", fields.GetProperty("name").GetString());
        Assert.Equal("StateIssueCustomField", fields.GetProperty("$type").GetString());
        var value = fields.GetProperty("value");
        Assert.Equal("Open", value.GetProperty("name").GetString());
        Assert.Equal("StateBundleElement", value.GetProperty("$type").GetString());
    }

    // ─── GetById / Search ─────────────────────────────────────────────────

    [Fact]
    public async Task GetById_Builds_Url_With_Fields()
    {
        var (yt, spy) = Fake("{\"idReadable\":\"TAM-1\",\"summary\":\"hello\"}");
        var iss = await yt.Issues.GetByIdAsync("TAM-1");
        Assert.Equal("TAM-1", iss.IdReadable);
        var req = spy.Requests.Single();
        Assert.Equal(HttpMethod.Get, req.Method);
        Assert.Equal("/api/issues/TAM-1", req.RequestUri!.AbsolutePath);
        Assert.Contains("fields=", req.RequestUri.Query);
    }

    [Fact]
    public async Task GetById_Custom_Fields_Override()
    {
        var (yt, spy) = Fake("{\"idReadable\":\"TAM-1\"}");
        await yt.Issues.GetByIdAsync("TAM-1", fields: "idReadable,summary");
        Assert.Contains("fields=idReadable,summary", spy.Requests.Single().RequestUri!.Query);
    }

    [Fact]
    public async Task GetById_Requires_Id()
    {
        var (yt, _) = Fake();
        await Assert.ThrowsAsync<ArgumentException>(() => yt.Issues.GetByIdAsync(""));
    }

    [Fact]
    public async Task Search_Builds_Query_Url()
    {
        var (yt, spy) = Fake("[{\"idReadable\":\"TAM-1\"},{\"idReadable\":\"TAM-2\"}]");
        var results = await yt.Issues.SearchAsync("project:TAM #Unresolved", top: 50);
        Assert.Equal(2, results.Count);
        var req = spy.Requests.Single();
        Assert.Equal("/api/issues", req.RequestUri!.AbsolutePath);
        Assert.Contains("query=project%3ATAM%20%23Unresolved", req.RequestUri.Query);
        Assert.Contains("top=50", req.RequestUri.Query);
    }

    [Fact]
    public async Task Search_Without_Top_Omits_The_Param()
    {
        var (yt, spy) = Fake("[]");
        await yt.Issues.SearchAsync("project:TAM");
        Assert.DoesNotContain("top=", spy.Requests.Single().RequestUri!.Query);
    }

    [Fact]
    public async Task Search_Null_Query_Throws()
    {
        var (yt, _) = Fake();
        await Assert.ThrowsAsync<ArgumentNullException>(() => yt.Issues.SearchAsync(null!));
    }

    // ─── Update / SetState ────────────────────────────────────────────────

    [Fact]
    public async Task Update_Requires_At_Least_One_Field()
    {
        var (yt, _) = Fake();
        await Assert.ThrowsAsync<ArgumentException>(() => yt.Issues.UpdateAsync("TAM-1"));
    }

    [Fact]
    public async Task Update_Summary_Only()
    {
        var (yt, spy) = Fake();
        await yt.Issues.UpdateAsync("TAM-1", summary: "renamed");
        var req = spy.Requests.Single();
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("/api/issues/TAM-1", req.RequestUri!.AbsolutePath);
        var body = JsonDocument.Parse(spy.RequestBodies.Single()).RootElement;
        Assert.Equal("renamed", body.GetProperty("summary").GetString());
    }

    [Fact]
    public async Task SetState_Emits_State_Custom_Field_With_Dollar_Type()
    {
        var (yt, spy) = Fake();
        await yt.Issues.SetStateAsync("TAM-1", "Done");
        var body = JsonDocument.Parse(spy.RequestBodies.Single()).RootElement;
        var f = body.GetProperty("customFields").EnumerateArray().Single();
        Assert.Equal("State", f.GetProperty("name").GetString());
        Assert.Equal("StateIssueCustomField", f.GetProperty("$type").GetString());
        Assert.Equal("Done", f.GetProperty("value").GetProperty("name").GetString());
        Assert.Equal("StateBundleElement", f.GetProperty("value").GetProperty("$type").GetString());
    }

    [Fact]
    public async Task SetState_Requires_StateName()
    {
        var (yt, _) = Fake();
        await Assert.ThrowsAsync<ArgumentException>(() => yt.Issues.SetStateAsync("TAM-1", ""));
    }

    // ─── Project helper ───────────────────────────────────────────────────

    [Fact]
    public async Task GetProjectByShortName_Returns_Match()
    {
        var responseBody = """[{"id":"0-2","shortName":"TAM","name":"Tamp"}]""";
        var (yt, spy) = Fake(responseBody);
        var p = await yt.Issues.GetProjectByShortNameAsync("TAM");
        Assert.NotNull(p);
        Assert.Equal("0-2", p!.Id);
        Assert.Equal("TAM", p.ShortName);
        var req = spy.Requests.Single();
        Assert.Equal("/api/admin/projects", req.RequestUri!.AbsolutePath);
        Assert.Contains("query=TAM", req.RequestUri.Query);
    }

    [Fact]
    public async Task GetProjectByShortName_Returns_Null_When_Not_Found()
    {
        var (yt, _) = Fake("[]");
        var p = await yt.Issues.GetProjectByShortNameAsync("NOPE");
        Assert.Null(p);
    }

    [Fact]
    public async Task GetProjectByShortName_Case_Insensitive()
    {
        // Query returns multiple candidates; case-insensitive match picks the one whose
        // shortName equals the input ignoring case.
        var responseBody = """[{"id":"0-9","shortName":"tampest"},{"id":"0-2","shortName":"TAM","name":"Tamp"}]""";
        var (yt, _) = Fake(responseBody);
        var p = await yt.Issues.GetProjectByShortNameAsync("tam");
        Assert.NotNull(p);
        Assert.Equal("0-2", p!.Id);
    }

    // ─── CustomFieldValue helpers ─────────────────────────────────────────

    [Fact]
    public void EnumByName_Builds_SingleEnumIssueCustomField_Shape()
    {
        var f = CustomFieldValue.EnumByName("Priority", "Critical");
        var json = JsonSerializer.Serialize(f.GetType()
            .GetMethod("ToWire", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(f, null));
        Assert.Contains("Priority", json);
        Assert.Contains("Critical", json);
        Assert.Contains("SingleEnumIssueCustomField", json);
        Assert.Contains("EnumBundleElement", json);
    }

    [Fact]
    public void SingleValue_Builds_SimpleIssueCustomField_Shape()
    {
        var f = CustomFieldValue.SingleValue("Story points", 8);
        var json = JsonSerializer.Serialize(f.GetType()
            .GetMethod("ToWire", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(f, null));
        Assert.Contains("SimpleIssueCustomField", json);
        Assert.Contains("Story points", json);
    }

    [Fact]
    public void Raw_Is_Passthrough()
    {
        var custom = new { name = "Foo", arbitrary = "shape" };
        var f = CustomFieldValue.Raw(custom);
        var json = JsonSerializer.Serialize(f.GetType()
            .GetMethod("ToWire", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(f, null));
        Assert.Contains("arbitrary", json);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private sealed class RecordingSpy : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> RequestBodies { get; } = new();
        public string ResponseBody { get; set; } = "{}";
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            RequestBodies.Add(body);
            Requests.Add(request);
            return new HttpResponseMessage(Status)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
