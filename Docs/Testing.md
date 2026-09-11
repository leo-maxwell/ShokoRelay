# Testing

The solution includes a small xUnit test project targeting .NET 10. Tests exercise the plugin assembly directly. Shoko Server and Plex are not required for the automated tests.

The suite and its infrastructure live on `subtitle-rename-with-tests`. The upstream feature branch, `subtitle-rename`, contains no tests or test dependencies. Feature changes flow in one direction: `subtitle-rename` → `subtitle-rename-with-tests` → `subtitle-rename-testing`. Merge feature changes into the tests branch before incorporating them into an installable testing build; never merge tests or testing-release changes back into `subtitle-rename`.

```sh
dotnet restore ShokoRelay.slnx
dotnet build ShokoRelay.slnx --no-restore -c Release
dotnet test ShokoRelay.Tests/ShokoRelay.Tests.csproj --no-build --no-restore -c Release
```

The Build & Test workflow runs these commands on Linux for pushes and pull requests targeting `subtitle-rename-with-tests` and can also be started manually. Filesystem fixtures are created beside the test assembly, so they use the checkout's volume rather than the system temporary volume. The physical case-only filename test probes that volume and is skipped only when it is case-insensitive. It runs locally on a case-sensitive macOS volume as well as in Linux CI. Case ambiguity and selection are also covered with in-memory filenames on every platform.

## Scope

- Rule-selection tests specify expected output-to-source mappings, including independent output priority, existing final suffixes, format preference, repeated input suffixes, case ambiguity passthrough, compound suffixes, empty rules, and non-chaining behavior. Reversed directory enumeration must give the same results.
- Configuration tests cover filename-safe suffix validation, persistence of ordered rules with repeated source suffixes, rejected saves, and safe loading of invalid externally edited rules. Malformed subtitle JSON values, including invalid Unicode, must preserve unrelated settings. Duplicate properties and ignored unknown fields retain the existing serializer behavior. Format preferences are checked on both load and save, including normalization, ignored invalid and unsupported entries, and defaults for older configurations. The dashboard schema must omit both subtitle options, while ordinary settings saves preserve them.
- Filesystem tests use temporary directories and the production linker and cleanup helpers. They verify symlink targets, repeated refreshes, rule and format reordering, removal of obsolete links, restoring original suffixes, unchanged source files, and exclusion of unsupported formats. Legacy sidecar names, including punctuation and space separators, survive with rules enabled or disabled. Missing and cyclic source links must not displace an available format or source; healthy symlink chains remain usable. Rejected output filenames must retain usable original-suffix links through cleanup, including long video basenames and repeated fallback destinations.

When changing these behaviors, add or update a test describing the observable output. A coverage percentage is not required. Keep test dependencies in the test project; the plugin should continue to load in Shoko without them.

## Formatting

The existing Lint & Format workflow also checks CSharpier, `dotnet format`, Prettier, and Stylelint with the Concentric configuration. Restore the pinned .NET tool before running C# checks:

```sh
dotnet tool restore
dotnet tool run csharpier check .
dotnet format style ShokoRelay.slnx --verify-no-changes --severity info
```

## Manual checks

Edit the subtitle fields inside `Advanced` in `preferences.json`, following the [configuration example](../README.md#subtitle-suffix-rules). Reload the dashboard and confirm that no subtitle editor appears. Change an ordinary setting and verify that the file retains both subtitle options. Check that a VFS refresh picks up file edits without restarting Shoko, including changing the preferred format and clearing the rules to restore original suffixes.

Before release, use a small series in Shoko to refresh TV and movie VFS links, inspect the resulting source targets, and verify subtitle detection and playback in Plex. Filename selection tests cannot establish Plex language recognition or playback behavior.

## Subtitle testing releases

This branch publishes a separate testing feed:

```text
https://raw.githubusercontent.com/leo-maxwell/ShokoRelay/subtitle-rename-testing/manifest.json
```

Before releasing, merge `subtitle-rename` into `subtitle-rename-with-tests`, then merge `subtitle-rename-with-tests` into `subtitle-rename-testing`. Keep manifest and publishing changes on the testing branch. Never merge either testing branch into the feature branch or upstream `master`.

Publish a GitHub prerelease from a tag such as `v0.17.5-dev.1001` on this branch. The first three version components must match `ShokoRelayConstants.Version`; testing revisions range from 1001 to 65534. Each build needs a new tag and version. The release workflow verifies branch ancestry, runs the tests, builds Linux ARM64, Linux x64, and Windows x64 archives with matching assembly versions, then updates only this branch's manifest. Completed release assets are not overwritten; rerun failed jobs or create a new version.

The testing build retains the official plugin UUID and reads the same configuration, including the Plex token. Back up the complete active configuration directory before switching. Repository installations normally use `<Shoko data directory>/configuration/2b0f5a7e-3d2b-4f3d-9e6b-7f0a6b2d8c9a/`; an existing `config` directory beside the plugin DLL takes precedence.

Keep official v0.17.3 installed for rollback. Install the testing release, select and enable that exact version, pin it, and restart Shoko. If disabling the old version manually, do so before enabling the testing version: both versions share Shoko's persistent enabled flag. Force-reload the dashboard and verify the active version and existing settings. Configure subtitle rules and format preferences in `preferences.json`, then start with filtered VFS refreshes. Rules saved by previous testing builds remain compatible. The build targets .NET 10 and `Shoko.Abstractions` 6.0.0-alpha.84, so use a compatible Shoko 6 daily build.

For rollback, select/enable and pin the official version, restore the backed-up configuration while Shoko is stopped if needed, and restart. Refresh affected VFS paths to restore original subtitle suffixes. Do not purge configuration when removing a version, because the configuration is shared.

The annotated tag [`subtitle-rename-ui-v1`](https://github.com/leo-maxwell/ShokoRelay/tree/subtitle-rename-ui-v1) preserves the complete working subtitle editor, including preview, autosave, and drag ordering, before the configuration-only revision. Future editor work can start from that tag; the active branches use the original upstream dashboard.
