using Tamp.Http;

namespace Tamp.YouTrack;

/// <summary>
/// Typed wrapper for the YouTrack REST API. Construct once per workspace with a permanent
/// token; navigate endpoint groups via the public properties.
///
/// <para>
/// The token is typed as <see cref="Secret"/> and sent as <c>Authorization: Bearer &lt;token&gt;</c>,
/// joined to the runner's redaction table so any log line that echoes the value is scrubbed.
/// </para>
///
/// <example>
/// <code>
/// using var yt = new YouTrackClient("https://your-workspace.youtrack.cloud/", PermanentToken);
/// var issue = await yt.Issues.CreateAsync(projectId: "0-2", summary: "Filed by Tamp", description: "...");
/// await yt.Issues.SetStateAsync(issue.IdReadable!, stateName: "Done");
/// </code>
/// </example>
/// </summary>
public sealed class YouTrackClient : TampApiClient
{
    /// <summary>The configured YouTrack base URL (with trailing slash). E.g. <c>https://workspace.youtrack.cloud/</c>.</summary>
    public Uri WorkspaceUrl => BaseUri;

    public YouTrackClient(string workspaceUrl, Secret permanentToken, bool disableConnectionVerification = false, HttpClient? http = null)
        : base(new Uri(workspaceUrl ?? throw new ArgumentNullException(nameof(workspaceUrl))),
               ApiCredential.Bearer(permanentToken),
               disableConnectionVerification,
               http,
               userAgent: "Tamp.YouTrack/0.1.0")
    {
        Issues = new IssuesClient(this);
    }

    /// <summary>Issue create / update / read / search.</summary>
    public IssuesClient Issues { get; }

    /// <summary>Escape hatch — GET an arbitrary YouTrack endpoint as a typed result. Path is relative to <see cref="WorkspaceUrl"/>.</summary>
    public Task<T> GetRawAsync<T>(string relativeUri, CancellationToken ct = default) => GetAsync<T>(relativeUri, ct);

    /// <summary>Escape hatch — POST an arbitrary endpoint with a JSON body and return a typed response.</summary>
    public Task<T> PostRawAsync<T>(string relativeUri, object body, CancellationToken ct = default) => PostJsonAsync<T>(relativeUri, body, ct);

    /// <summary>Escape hatch — POST an arbitrary endpoint with a JSON body and ignore the response.</summary>
    public Task PostRawAsync(string relativeUri, object body, CancellationToken ct = default) => PostJsonAsync(relativeUri, body, ct);

    // Internal pass-throughs for nested endpoint clients (mirror Tamp.AdoRest.V7's shape).
    internal Task<T> GetInternal<T>(string relativeUri, CancellationToken ct) => GetAsync<T>(relativeUri, ct);
    internal Task<T> PostInternal<T>(string relativeUri, object body, CancellationToken ct) => PostJsonAsync<T>(relativeUri, body, ct);
    internal Task PostInternal(string relativeUri, object body, CancellationToken ct) => PostJsonAsync(relativeUri, body, ct);
}
