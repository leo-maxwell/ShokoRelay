# Testing

The solution includes a small xUnit test project targeting .NET 10. Tests exercise the plugin assembly directly. Shoko Server and Plex are not required for the automated tests.

```sh
dotnet restore ShokoRelay.slnx
dotnet build ShokoRelay.slnx --no-restore -c Release
dotnet test ShokoRelay.Tests/ShokoRelay.Tests.csproj --no-build --no-restore -c Release
```

The Build & Test workflow runs these commands on Linux for pull requests targeting `master` and can also be started manually. Filesystem fixtures are created beside the test assembly, so they use the checkout's volume rather than the system temporary volume. The physical case-only filename test probes that volume and is skipped only when it is case-insensitive. It runs locally on a case-sensitive macOS volume as well as in Linux CI. Case ambiguity and selection are also covered with in-memory filenames on every platform.

## Scope

- Rule-selection tests specify expected output-to-source mappings, including independent output priority, existing final suffixes, format preference, repeated input suffixes, case ambiguity passthrough, compound suffixes, empty rules, and non-chaining behavior. Reversed directory enumeration must give the same results.
- Configuration tests cover filename-safe suffix validation, persistence of ordered rules with repeated source suffixes, rejected saves, and safe loading of invalid externally edited rules.
- Preview request tests exercise MVC validation of series IDs without running a server.
- Filesystem tests use temporary directories and the production linker and cleanup helpers. They verify symlink targets, repeated refreshes, rule reordering, removal of obsolete links, restoring original suffixes, and unchanged source files.

When changing these behaviors, add or update a test describing the observable output. A coverage percentage is not required. Keep test dependencies in the test project; the plugin should continue to load in Shoko without them.

## Formatting

The existing Lint & Format workflow also checks CSharpier, `dotnet format`, Prettier, and Stylelint with the Concentric configuration. Restore the pinned .NET tool before running C# checks:

```sh
dotnet tool restore
dotnet tool run csharpier check .
dotnet format ShokoRelay.slnx --verify-no-changes --severity info
```

## Manual checks

For dashboard changes, check that complete, valid edits save when leaving a field, and that reordering and removal save immediately. Partial or invalid edits must leave saved rules intact. Check rapid consecutive moves, edits during a pending save, save failures and retry, and persistence after reloading. Include a change to another setting while a rule save is pending to verify configuration requests preserve their order. Preview requests must not save settings or modify VFS files or the blueprint cache; the usual field-change autosave may run when clicking Preview moves focus away from an edited field.

Before release, use a small series in Shoko to refresh TV and movie VFS links, inspect the resulting source targets, and verify subtitle detection and playback in Plex. Filename selection tests cannot establish Plex language recognition or playback behavior.

## Subtitle testing releases

This branch publishes a separate testing feed:

```text
https://raw.githubusercontent.com/leo-maxwell/ShokoRelay/subtitle-rename-testing/manifest.json
```

Feature fixes belong on `subtitle-rename`; merge that branch into `subtitle-rename-testing` before releasing. Keep these manifest and publishing changes on the testing branch. Never merge the testing branch into the feature branch or upstream `master`.

Publish a GitHub prerelease from a tag such as `v0.17.4-dev.1001` on this branch. The first three version components must match `ShokoRelayConstants.Version`; testing revisions range from 1001 to 65534. Each build needs a new tag and version. The release workflow verifies branch ancestry, runs the tests, builds Linux ARM64, Linux x64, and Windows x64 archives with matching assembly versions, then updates only this branch's manifest. Completed release assets are not overwritten; rerun failed jobs or create a new version.

The testing build retains the official plugin UUID and reads the same configuration, including the Plex token. Back up the complete active configuration directory before switching. Repository installations normally use `<Shoko data directory>/configuration/2b0f5a7e-3d2b-4f3d-9e6b-7f0a6b2d8c9a/`; an existing `config` directory beside the plugin DLL takes precedence.

Keep official v0.17.3 installed for rollback. Install the testing release, select and enable that exact version, pin it, and restart Shoko. If disabling the old version manually, do so before enabling the testing version: both versions share Shoko's persistent enabled flag. Force-reload the dashboard, verify the active version and existing settings, then start with preview and filtered VFS refreshes. The build targets .NET 10 and `Shoko.Abstractions` 6.0.0-alpha.84, so use a compatible Shoko 6 daily build.

For rollback, select/enable and pin the official version, restore the backed-up configuration while Shoko is stopped if needed, and restart. Refresh affected VFS paths to restore original subtitle suffixes. Do not purge configuration when removing a version, because the configuration is shared.
