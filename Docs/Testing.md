# Testing

The xUnit project targets .NET 10 and exercises the production configuration provider, subtitle linker, symlinks, blueprint callbacks, and cleanup helpers. Shoko Server and Plex are not required for automated tests.

The suite and its infrastructure live on `subtitle-rename-with-tests`. The upstream feature branch, `subtitle-rename`, contains no tests or test dependencies. Changes flow in one direction: `subtitle-rename` → `subtitle-rename-with-tests` → `subtitle-rename-testing`. Merge feature changes into the tests branch before incorporating them into the installable testing branch. Never merge tests or testing-release changes back into the feature branch.

```sh
dotnet restore ShokoRelay.slnx
dotnet build ShokoRelay.slnx --no-restore -c Release
dotnet test ShokoRelay.Tests/ShokoRelay.Tests.csproj --no-build --no-restore -c Release
```

The Build & Test workflow runs these commands on Linux for pushes and pull requests targeting `subtitle-rename-with-tests`, and can also be started manually. Filesystem fixtures use the checkout volume, beside the test assembly. Physical case-only filename tests are skipped only when that volume is case-insensitive. They run on the local case-sensitive macOS workspace and in Linux CI.

## Behavior covered

- Real link targets specify existing-name precedence and mapping-order priority. Reversing discovery order must not change the winner. Different extensions and flags remain independent; no format is ranked or suppressed merely because another format exists.
- Language-token matching is literal and case-insensitive. Flags are preserved, aliases are explicit, and replacements are not processed recursively. A source creates at most one output. Tests cover identity, circular, multi-token, and duplicate-case mappings.
- NFO files and artwork retain their names. Legacy subtitle separators and suffixless subtitles remain usable. Alphanumeric continuations of the video basename cannot be claimed by a prefix match; punctuation-separated names retain the existing discovery behavior. Unsupported subtitle formats remain unsupported.
- Reordering mappings, clearing them, repeated refreshes, and stale VFS links exercise actual symlink replacement and orphan cleanup. Source contents remain unchanged. The same linker is tested with TV and movie destination names, existence-check modes, and blueprint-only mode.
- Missing, cyclic, or directory subtitle targets cannot win collisions. Healthy source symlink chains retain their immediate source link. Rejected destination filenames fall back to the original suffix and survive cleanup. If both destinations fail, the failure is reported and other subtitles still link.
- Standard JSON serialization and configuration saves preserve mapping order and values. Missing or obsolete settings do not enable mappings. Invalid JSON or incompatible mapping types follow the existing whole-configuration defaults path without rewriting the file. Nulls, duplicate properties, trailing commas, and unknown fields follow the standard serializer behavior. The dashboard schema omits subtitle mappings; ordinary settings saves preserve them.

The old planner tests, format-preference tests, multiple-output tests, and custom-converter isolation expectations have been replaced with these observable requirements. Test code sets the existing static configuration provider through reflection, restores it after each fixture, and disables parallel execution for linker fixtures; production code does not expose a test-only settings parameter.

## Scope alignment

The implementation follows the maintainer's small language-mapping approach: no dedicated rule class, custom converter, format ranking, multiple-output feature, separate planner file, or settings parameters threaded through VFS builders. Existing discovery/cache, linking, and cleanup remain in use.

Two deliberate requirements refine the proposed sample: mapping order resolves colliding complete destination names after unchanged originals, and a built-in ordered dictionary preserves that order. Losing conversions are omitted, so collisions are an explicit exception to mirroring every valid sidecar. Case-insensitive matching, subtitle-only filtering, and preserved flags are part of the agreed scope.

## Formatting

```sh
dotnet tool restore
dotnet tool run csharpier check .
dotnet format style ShokoRelay.slnx --verify-no-changes --severity info
```

The upstream Lint & Format workflow also checks .NET style, Prettier, and Stylelint. Existing upstream findings must be distinguished from diagnostics introduced by this change; do not fix unrelated formatting as part of the subtitle feature.

## Manual integration checks

Edit `Advanced.SubtitleLanguageMappings` in `preferences.json`, following the [configuration example](../README.md#subtitle-language-mappings). Reload the dashboard, change an ordinary setting, and confirm mapping order and values survive. Refresh a small TV series and a movie; inspect subtitle names and targets after reordering and clearing mappings. Verify that the existing file watcher reloads settings without restarting Shoko.

Plex language recognition and playback require a live Shoko/Plex installation; automated filename and linker tests do not establish either. Testing-release publishing is a separate step, using only the testing branch's manifest and release workflow.
