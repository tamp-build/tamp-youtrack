# Changelog

All notable changes to **Tamp.YouTrack** are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versions follow [SemVer](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.2.0] - 2026-11-20 - .NET 11 in, .NET 8 and .NET 9 out

### Changed

- **Multi-targets `net10.0;net11.0`** instead of `net8.0;net9.0;net10.0` ([ADR 0015](https://github.com/tamp-build/tamp/blob/main/docs/adr/0015-target-framework-strategy.md)). .NET 11 shipped 2026-11-10 and .NET 8 and .NET 9 reached end of support the same day; the rule is to add a TFM the day a release ships and drop one the day Microsoft ends its support. `global.json` pins SDK 11.0.100.

  **If you target `net8.0` or `net9.0`, stay on the previous version** — this one carries no assets your project can resolve.

- **`Tamp.*` pinned to 1.21.0**, which carries a SARIF fix worth knowing about if this project's output is read as evidence: before 1.21.0, a SARIF result whose scanner omitted `level` was read as `none` — the least severe value — instead of the declared `warning`, on every runtime before net11 ([tamp#166](https://github.com/tamp-build/tamp/issues/166)).

- **Publishing moved to nuget.org trusted publishing (OIDC).** The `tamp_nuget_api_key` org secret expired 2026-11-01 along with every other pre-2026-08-17 nuget.org key. The publish job now mints a short-lived key from its own OIDC token, so there is no long-lived credential to rotate or leak.

## [0.1.1] — 2026-09-27

### Added

- Package now ships XML documentation files (`.xml`) alongside the assembly, so consumers get IntelliSense and API docs. (Mirrors [tamp-build/tamp#3](https://github.com/tamp-build/tamp/pull/50).)


## [0.1.0] - 2026-05-13

### Added

- Initial release. Typed library wrapping the YouTrack REST API for Tamp
  build scripts. Filed under TAM-180. Pins to `Tamp.Http` for the shared
  `TampApiClient` substrate (auth / JSON / Secret-redacted error mapping).

#### `YouTrackClient`

- Bearer auth via `Secret`-typed permanent token. Joined to Tamp's runner
  redaction table — values are scrubbed from any log line.
- Workspace URL is normalized to ensure a trailing slash.
- Escape hatches: `GetRawAsync<T>`, `PostRawAsync<T>`, `PostRawAsync` for
  verbs not yet typed.

#### `Issues` endpoint group

- **`CreateAsync(projectId, summary, description?, customFields?)`** —
  `POST /api/issues`. Throws on empty `projectId` or `summary`.
- **`UpdateAsync(id, summary?, description?, customFields?)`** —
  `POST /api/issues/{id}`. Partial update; throws if all three optional
  parameters are null/empty (YouTrack rejects empty bodies).
- **`SetStateAsync(id, stateName)`** — convenience over `UpdateAsync`
  wrapping a `CustomFieldValue.StateByName` payload.
- **`GetByIdAsync(id, fields?)`** — `GET /api/issues/{id}`. Accepts
  readable (`"TAM-180"`) or internal IDs. Default fields are
  `idReadable,summary,description,customFields(name,value(name))`.
- **`SearchAsync(query, top?, fields?)`** — `GET /api/issues?query=...`.
  Uses YouTrack's search syntax (`project:TAM #Unresolved`).
- **`GetProjectByShortNameAsync(shortName)`** — case-insensitive resolver
  for internal project ID. Returns null if not found.

#### `CustomFieldValue` factories

Build the heterogeneous `customFields` wire shape with `$type` discriminators
correctly placed:

- **`StateByName(name)`** — State field (most common).
- **`EnumByName(fieldName, valueName)`** — Priority / Type / custom enums.
- **`SingleValue(fieldName, value)`** — scalar text / int / float.
- **`Raw(anonymousObject)`** — escape hatch for Period, Date, multi-enum,
  user-reference, etc.

The `$type` key cannot be emitted directly from C# anonymous-object syntax;
the factories post-process via `System.Text.Json.Nodes` to rename `type` →
`$type` at the right paths. Adopters never see the mechanics.

### Tests

- 24 unit tests covering: construction + Bearer header, create body shape,
  GetById URL composition, search query encoding, partial-update validation,
  SetState wire shape including `$type` placement, project short-name
  resolution (case-insensitive + not-found), `CustomFieldValue` factory
  output shapes.

### Requires

- **Tamp.Core ≥ 1.6.0** (no per-satellite IVT needed; Secret.Reveal is public).
- **Tamp.Http ≥ 0.1.1** (TampApiClient base).

### Notes

- Originally filed as "candidate, reassess if 2+ adopters ask" — the
  tamp-build org itself is the first adopter (via its self-managed YT
  workspace). Shipping at 0.1.0 minimum-useful surface; expand on demand.

- Wire-shape gotchas captured in the README from real adoption experience:
  `$type` discriminator, value heterogeneity, State command syntax,
  fields= mandatory, project short-name → ID translation.
