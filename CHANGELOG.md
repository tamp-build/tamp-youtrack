# Changelog

All notable changes to **Tamp.YouTrack** are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versions follow [SemVer](https://semver.org/spec/v2.0.0.html).

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
