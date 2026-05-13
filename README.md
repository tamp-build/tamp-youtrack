# Tamp.YouTrack

> Typed library-mode wrapper for the [YouTrack REST API](https://www.jetbrains.com/help/youtrack/devportal/youtrack-rest-api.html). Build scripts can file / update / search / set-state on issues without rolling raw HTTP. Auth is a `Secret`-typed Bearer permanent token. Built on `Tamp.Http`.

| Package | Status |
|---|---|
| `Tamp.YouTrack` | 0.1.0 (initial) |

## Why this exists

The Tamp project itself (and the tamp-build org's satellite repos) lives in YouTrack. Across this onboarding wave I filed and updated 40+ tickets via raw HTTP with `curl` — each invocation needed the `$type` discriminator on inner objects, the heterogeneous `customFields` value shape, the state-name-only command syntax, and the bundle-ID lookup ceremony for State / Type / Priority enum values. Every adopter who lives in YouTrack hits the same surface and reinvents the same wrapper.

`Tamp.YouTrack` ships the minimum-useful surface as a typed library:

- **`yt.Issues.CreateAsync(projectId, summary, description, customFields)`** — file a new issue
- **`yt.Issues.UpdateAsync(id, summary?, description?, customFields?)`** — partial-update an existing issue
- **`yt.Issues.SetStateAsync(id, stateName)`** — convenience for the most common update
- **`yt.Issues.GetByIdAsync(id, fields?)`** — read an issue
- **`yt.Issues.SearchAsync(query, top?, fields?)`** — query with YouTrack's `project:TAM #Unresolved` syntax
- **`yt.Issues.GetProjectByShortNameAsync(shortName)`** — resolve `"TAM"` → `"0-2"` so adopters don't hard-code internal IDs

Plus typed `CustomFieldValue` factories for the common write shapes (`StateByName`, `EnumByName`, `SingleValue`) and a `Raw` escape hatch for the long tail.

## Install

```bash
dotnet add package Tamp.YouTrack
```

Multi-targets net8 / net9 / net10. Requires `Tamp.Core` ≥ **1.6.0** and `Tamp.Http` ≥ 0.1.1.

## Quick start — file a ticket from a Tamp target

```csharp
using Tamp;
using Tamp.YouTrack;

class Build : TampBuild
{
    public static int Main(string[] args) => Execute<Build>(args);

    [Parameter] readonly string YouTrackUrl = "https://your-workspace.youtrack.cloud/";
    [Secret] readonly Secret YouTrackToken = null!;

    AbsolutePath Artifacts => RootDirectory / "artifacts";

    // After a release, file a tracking ticket for the next iteration.
    Target FileFollowUp => _ => _
        .Description("[Release] Open a YouTrack ticket for the next milestone's work")
        .Executes(async () =>
        {
            using var yt = new YouTrackClient(YouTrackUrl, YouTrackToken);

            // Resolve project short-name → internal ID. ("TAM" → "0-2" for tamp-build's workspace.)
            var project = await yt.Issues.GetProjectByShortNameAsync("TAM")
                ?? throw new InvalidOperationException("Project TAM not found.");

            var issue = await yt.Issues.CreateAsync(
                projectId: project.Id!,
                summary: $"Next milestone planning — {DateTime.UtcNow:yyyy-MM-dd}",
                description: "Auto-filed by Tamp's release pipeline. Owner: TBD.");

            Console.WriteLine($"Filed {issue.IdReadable}");
        });

    // After a successful Push target, mark the originating ticket Done.
    Target CloseShipTicket => _ => _
        .Description("[Release] Mark TAM-N Done after Push succeeds")
        .Executes(async () =>
        {
            using var yt = new YouTrackClient(YouTrackUrl, YouTrackToken);
            await yt.Issues.SetStateAsync("TAM-198", "Done");
        });
}
```

## Verb surface

### `Issues` endpoint group

| Method | YouTrack endpoint | Notes |
|---|---|---|
| `CreateAsync(projectId, summary, description?, customFields?)` | `POST /api/issues` | `projectId` is the internal ID (e.g. `"0-2"`). Use `GetProjectByShortNameAsync` to resolve from `"TAM"`. |
| `UpdateAsync(id, summary?, description?, customFields?)` | `POST /api/issues/{id}` | Partial update — pass only the fields you want changed. Throws if all three are null/empty. |
| `SetStateAsync(id, stateName)` | `POST /api/issues/{id}` | Convenience for the most common Update — wraps a `CustomFieldValue.StateByName` payload. |
| `GetByIdAsync(id, fields?)` | `GET /api/issues/{id}` | `id` can be readable (`"TAM-180"`) or internal. Default fields are id/summary/description/customFields(name,value(name)). |
| `SearchAsync(query, top?, fields?)` | `GET /api/issues?query=...` | Query uses [YouTrack search syntax](https://www.jetbrains.com/help/youtrack/devportal/search-query-grammar.html). Common: `project:TAM #Unresolved`, `assignee:me #Open`, `created:Today`. |
| `GetProjectByShortNameAsync(shortName)` | `GET /api/admin/projects?query=...` | Case-insensitive match on `shortName`. Returns null if not found. |

### `CustomFieldValue` factories

YouTrack's custom-field wire shape is heterogeneous: the `$type` discriminator names the field kind (StateIssueCustomField / SingleEnumIssueCustomField / SimpleIssueCustomField / ...), and the inner `value` carries an analogous `$type`. The factories build the right shape:

| Factory | What it builds | Use for |
|---|---|---|
| `CustomFieldValue.StateByName("Done")` | `{ name: "State", $type: "StateIssueCustomField", value: { name: "Done", $type: "StateBundleElement" } }` | State workflow |
| `CustomFieldValue.EnumByName("Priority", "Critical")` | `SingleEnumIssueCustomField` shape | Priority, Type, Subsystem, custom enums |
| `CustomFieldValue.SingleValue("Story points", 8)` | `SimpleIssueCustomField` shape | Numeric / text scalar fields |
| `CustomFieldValue.Raw(anonymousObject)` | passes the object through verbatim | escape hatch for the long tail (Period, Date, multi-enum, user-references, etc.) |

### Raw HTTP escape hatch

For verbs not yet typed:

```csharp
var tagged = await yt.GetRawAsync<JsonElement>(
    "api/issues?query=tag:%23release&fields=idReadable,summary&top=20");
```

`GetRawAsync<T>`, `PostRawAsync<T>`, `PostRawAsync` take a relative URI and a JSON-serializable body.

## Authentication

YouTrack permanent tokens (`perm-...`) are the canonical Bearer credential. Generate one from your YouTrack profile → Authentication → New token. Pass as a `Tamp.Core.Secret`:

```csharp
[Secret(EnvironmentVariable = "YOUTRACK_TOKEN")]
readonly Secret YouTrackToken = null!;

using var yt = new YouTrackClient("https://workspace.youtrack.cloud/", YouTrackToken);
```

The token routes via `Authorization: Bearer <revealed>` and is registered with Tamp's redaction table so any echoed log line gets scrubbed.

## YouTrack quirks worth knowing

Captured during the tamp-build adoption that informed this satellite's design:

- **`$type` is the discriminator.** YouTrack's REST uses `$type` (not `@type` / `kind` / `type`) on custom-field rows and on nested values. C# anonymous-object syntax can't emit `$type` directly; the satellite's `CustomFieldValue` factories handle the rename via internal JSON-node post-processing.
- **State commands use value-alone syntax.** `State Done` works; `State {Done}` (with braces) is rejected. The `SetStateAsync` convenience method gets this right; if you use raw `UpdateAsync` with a `StateByName` payload, follow the same pattern.
- **`customFields` shape varies by field kind.** Don't try to deserialize the entire `customFields` array into a single strongly-typed model. Use `Issue.CustomFields` which yields `IssueCustomField { Name, Value: JsonElement }` — interpret per-name.
- **`fields=` query is mandatory in practice.** A bare `GET /api/issues/{id}` returns only the internal ID. Always set `fields=idReadable,summary,...` either explicitly or via the satellite's defaults.
- **Project short-names need translation.** `POST /api/issues` accepts only the internal project ID (e.g. `"0-2"`), not the short name (`"TAM"`). `GetProjectByShortNameAsync` does the resolution.

## Pairs with

- [`Tamp.Http`](https://github.com/tamp-build/tamp-http) — the `TampApiClient` base class. `YouTrackClient` derives from it for shared auth / JSON / redaction / error mapping.
- [`Tamp.AdoRest.V7`](https://github.com/tamp-build/tamp-ado-rest) — sibling satellite for Azure DevOps; same shape, different REST surface.
- [`Tamp.Kudu`](https://github.com/tamp-build/tamp-kudu) — sibling satellite for Azure App Service Kudu + Management API.

## What's NOT in 0.1.0

- **Workflow operations** (apply transitions explicitly, vote, tag) — file via raw API for now.
- **Attachments** — multipart upload deferred until an adopter asks.
- **Comments** — same.
- **Time tracking** — same.
- **Agile / sprint operations** — same.

The 0.1.0 surface is the minimum that lets a Tamp build script file and manage tickets in its own YouTrack workspace. Expand on adopter demand — open an issue with the use case.

## Releasing

Releases follow the [Tamp dogfood pattern](MAINTAINERS.md).

## License

MIT. See [LICENSE](LICENSE).
