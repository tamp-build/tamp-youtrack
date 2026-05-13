using System.Text.Json.Serialization;

namespace Tamp.YouTrack;

/// <summary>Endpoint group for <c>/api/issues</c>.</summary>
public sealed class IssuesClient
{
    private readonly YouTrackClient _c;
    internal IssuesClient(YouTrackClient client) => _c = client;

    // ─── Create ───────────────────────────────────────────────────────────

    /// <summary>
    /// <c>POST /api/issues</c> — create a new issue in <paramref name="projectId"/>. Returns the
    /// freshly-created issue with at minimum <see cref="Issue.IdReadable"/> and
    /// <see cref="Issue.Summary"/> populated.
    /// </summary>
    /// <param name="projectId">YouTrack internal project ID (e.g. <c>"0-2"</c>). Use <see cref="GetProjectByShortNameAsync"/> if you only have the short name.</param>
    /// <param name="summary">Issue title (required).</param>
    /// <param name="description">Markdown-supporting body. Optional.</param>
    /// <param name="customFields">Optional custom-field payloads — pass typed values via <see cref="CustomFieldValue"/> helpers.</param>
    public async Task<Issue> CreateAsync(
        string projectId,
        string summary,
        string? description = null,
        IReadOnlyList<CustomFieldValue>? customFields = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(projectId)) throw new ArgumentException("projectId is required.", nameof(projectId));
        if (string.IsNullOrEmpty(summary)) throw new ArgumentException("summary is required.", nameof(summary));

        var body = new CreateIssueBody(
            Project: new ProjectRef(projectId),
            Summary: summary,
            Description: description,
            CustomFields: customFields?.Select(f => f.ToWire()).ToArray());

        return await _c.PostInternal<Issue>(
            "api/issues?fields=" + DefaultIssueFields,
            body, ct).ConfigureAwait(false);
    }

    // ─── Read ──────────────────────────────────────────────────────────────

    /// <summary><c>GET /api/issues/{id}</c> — fetch an issue by readable ID (e.g. <c>"TAM-180"</c>) or internal ID.</summary>
    public Task<Issue> GetByIdAsync(string id, string? fields = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(id)) throw new ArgumentException("id is required.", nameof(id));
        var f = fields ?? DefaultIssueFields;
        return _c.GetInternal<Issue>($"api/issues/{Uri.EscapeDataString(id)}?fields={f}", ct);
    }

    /// <summary>
    /// <c>GET /api/issues?query=...</c> — search using YouTrack's query syntax.
    /// Example: <c>"project:TAM #Unresolved"</c>.
    /// </summary>
    public async Task<IReadOnlyList<Issue>> SearchAsync(string query, int? top = null, string? fields = null, CancellationToken ct = default)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));
        var f = fields ?? DefaultIssueFields;
        var topPart = top is null ? string.Empty : $"&top={top}";
        var url = $"api/issues?query={Uri.EscapeDataString(query)}&fields={f}{topPart}";
        var result = await _c.GetInternal<Issue[]>(url, ct).ConfigureAwait(false);
        return result;
    }

    // ─── Update ────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>POST /api/issues/{id}</c> — partial update. Pass only the fields you want to change.
    /// YouTrack's REST treats this as a merge; unset fields are unchanged.
    /// </summary>
    public Task<Issue> UpdateAsync(
        string id,
        string? summary = null,
        string? description = null,
        IReadOnlyList<CustomFieldValue>? customFields = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(id)) throw new ArgumentException("id is required.", nameof(id));

        // YouTrack rejects an empty body — at least one updatable property is required.
        if (summary is null && description is null && (customFields is null || customFields.Count == 0))
            throw new ArgumentException(
                "At least one of summary, description, or customFields must be supplied.",
                nameof(customFields));

        var body = new UpdateIssueBody(
            Summary: summary,
            Description: description,
            CustomFields: customFields?.Select(f => f.ToWire()).ToArray());

        return _c.PostInternal<Issue>(
            $"api/issues/{Uri.EscapeDataString(id)}?fields={DefaultIssueFields}",
            body, ct);
    }

    /// <summary>
    /// Convenience: set the State field to a named value (e.g. <c>"Done"</c>, <c>"Open"</c>, <c>"In Progress"</c>).
    /// Equivalent to <see cref="UpdateAsync"/> with a single <see cref="CustomFieldValue.StateByName"/> entry.
    /// </summary>
    public Task<Issue> SetStateAsync(string id, string stateName, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(stateName)) throw new ArgumentException("stateName is required.", nameof(stateName));
        return UpdateAsync(id,
            customFields: new[] { CustomFieldValue.StateByName(stateName) },
            ct: ct);
    }

    // ─── Project helper ────────────────────────────────────────────────────

    /// <summary>
    /// <c>GET /api/admin/projects?fields=id,shortName,name&amp;query=...</c> — resolve a project's
    /// internal ID from its short name (e.g. <c>"TAM"</c>). YouTrack's create-issue endpoint expects
    /// the internal ID; adopters usually only know the short name.
    /// </summary>
    public async Task<Project?> GetProjectByShortNameAsync(string shortName, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(shortName)) throw new ArgumentException("shortName is required.", nameof(shortName));
        var url = $"api/admin/projects?fields=id,shortName,name&query={Uri.EscapeDataString(shortName)}";
        var projects = await _c.GetInternal<Project[]>(url, ct).ConfigureAwait(false);
        return projects.FirstOrDefault(p =>
            string.Equals(p.ShortName, shortName, StringComparison.OrdinalIgnoreCase));
    }

    // The default fields query used by every method that doesn't override. Adopters who want
    // more (custom-field values, comments, attachments) pass `fields` explicitly.
    internal const string DefaultIssueFields = "idReadable,summary,description,customFields(name,value(name))";
}

// ───────────────────────────────────────────────────────────────────────────
//  Wire models — public so adopters can deserialize from GetRawAsync calls.
// ───────────────────────────────────────────────────────────────────────────

/// <summary>YouTrack issue wire model. Most fields are nullable because YouTrack returns only what's in <c>fields=</c>.</summary>
public sealed record Issue
{
    [JsonPropertyName("idReadable")] public string? IdReadable { get; init; }
    [JsonPropertyName("id")] public string? Id { get; init; }
    [JsonPropertyName("summary")] public string? Summary { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("customFields")] public IssueCustomField[]? CustomFields { get; init; }
}

/// <summary>One row of the <c>customFields</c> array on an Issue.</summary>
public sealed record IssueCustomField
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("value")] public System.Text.Json.JsonElement Value { get; init; }
}

/// <summary>YouTrack project wire model.</summary>
public sealed record Project
{
    [JsonPropertyName("id")] public string? Id { get; init; }
    [JsonPropertyName("shortName")] public string? ShortName { get; init; }
    [JsonPropertyName("name")] public string? Name { get; init; }
}

// ───────────────────────────────────────────────────────────────────────────
//  CustomFieldValue — adopter-facing typed payloads for custom-field writes.
//  YouTrack's customFields wire shape is heterogeneous ($type discriminator
//  per field kind; State/Enum reference by name, SingleValue carries scalar).
//  We surface the common kinds adopters use.
// ───────────────────────────────────────────────────────────────────────────

/// <summary>
/// One custom-field assignment for create/update. Use the static factories — the constructor
/// is internal because the on-wire shape depends on the field kind.
/// </summary>
public sealed class CustomFieldValue
{
    private readonly object _wire;
    internal CustomFieldValue(object wire) => _wire = wire;
    internal object ToWire() => _wire;

    /// <summary>Set a State-typed field by the state's display name (e.g. <c>"Done"</c>, <c>"In Progress"</c>).</summary>
    public static CustomFieldValue StateByName(string stateName) =>
        new(new
        {
            name = "State",
            type = "StateIssueCustomField",
            value = new { name = stateName, type = "StateBundleElement" },
        }.WithDollarTypes(("type", "$type"), ("value.type", "$type")));

    /// <summary>Set an enum-typed field (Priority, Type, etc.) by the value's name.</summary>
    public static CustomFieldValue EnumByName(string fieldName, string valueName) =>
        new(new
        {
            name = fieldName,
            type = "SingleEnumIssueCustomField",
            value = new { name = valueName, type = "EnumBundleElement" },
        }.WithDollarTypes(("type", "$type"), ("value.type", "$type")));

    /// <summary>Set a scalar single-value field (text / integer / float) by its name.</summary>
    public static CustomFieldValue SingleValue(string fieldName, object value) =>
        new(new
        {
            name = fieldName,
            type = "SimpleIssueCustomField",
            value = value,
        }.WithDollarTypes(("type", "$type")));

    /// <summary>Escape hatch: pass an arbitrary anonymous object as the on-wire payload.</summary>
    public static CustomFieldValue Raw(object payload) => new(payload);
}

// ───────────────────────────────────────────────────────────────────────────
//  Internal wire-shape helpers.
// ───────────────────────────────────────────────────────────────────────────

internal sealed record CreateIssueBody(
    [property: JsonPropertyName("project")] ProjectRef Project,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("customFields")] object[]? CustomFields);

internal sealed record UpdateIssueBody(
    [property: JsonPropertyName("summary")] string? Summary,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("customFields")] object[]? CustomFields);

internal sealed record ProjectRef([property: JsonPropertyName("id")] string Id);

internal static class WireShapeExtensions
{
    /// <summary>
    /// Translate selected keys to <c>$type</c> after construction. YouTrack uses <c>$type</c> as the
    /// JSON discriminator; C# anonymous-object syntax can't emit a property named <c>$type</c>
    /// directly. This helper post-processes the anonymous object into a dictionary with the keys
    /// renamed. <see cref="System.Text.Json"/> serializes dictionaries by key, so the resulting
    /// payload is the YouTrack-expected shape.
    /// </summary>
    public static object WithDollarTypes(this object source, params (string DotPath, string ReplacementKey)[] mappings)
    {
        // Build a dictionary tree from the anonymous object via JSON round-trip.
        var json = System.Text.Json.JsonSerializer.Serialize(source);
        var node = System.Text.Json.Nodes.JsonNode.Parse(json) ?? throw new InvalidOperationException("Failed to parse anonymous object.");
        foreach (var (dotPath, replacementKey) in mappings)
        {
            ApplyRename(node, dotPath.Split('.'), replacementKey, originalKey: dotPath.Split('.')[^1]);
        }
        return node;
    }

    private static void ApplyRename(System.Text.Json.Nodes.JsonNode? node, ReadOnlySpan<string> path, string newKey, string originalKey)
    {
        if (node is null) return;
        if (path.Length == 1)
        {
            if (node is System.Text.Json.Nodes.JsonObject obj && obj.ContainsKey(originalKey))
            {
                var v = obj[originalKey];
                obj.Remove(originalKey);
                obj[newKey] = v?.DeepClone();
            }
            return;
        }
        if (node is System.Text.Json.Nodes.JsonObject parent && parent.TryGetPropertyValue(path[0], out var child))
        {
            ApplyRename(child, path[1..], newKey, originalKey);
        }
    }
}
