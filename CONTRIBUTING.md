# Contributing

Keep display changes isolated from cooling, fan, firmware, and driver configuration. Preserve existing recovery files until restoration succeeds. Do not commit real user media, local HP settings, logs, or proprietary HP binaries.

Before sending a change, run `build.ps1 -Test`. For media-related changes also run `tests/integration.ps1` on a machine with HP's installed converter. Hardware tests are opt-in through `-Hardware`.

For bug reports, include the PC model, Windows version, OMEN Gaming Hub version, file type and dimensions, composition settings, reproduction steps, and whether Restore worked. Redact names and personal file paths from logs before sharing. Do not attach copyrighted/private media unless you can share it; a synthetic fixture is preferable.

Source layout:

- `MainForm.cs`: editor, tray lifecycle, stats display.
- `PreviewCanvas.cs`: circular animated preview and mouse gestures.
- `MediaTransform.cs`: composition geometry shared by rendering and preview.
- `MediaService.cs`: asynchronous preview/export and conversion cancellation.
- `HpDisplayBackend.cs`: backups, profile updates, helper restarts, restoration.
- `AppState.cs`: local preferences and logging.
- `DashboardForm.cs`, `DashboardModel.cs`, `DashboardRenderer.cs`: stats designer, assignments and layouts.
- `SystemMetrics.cs`, `DashboardRuntime.cs`: hardware readings and background polling.
- `CustomMetricServer.cs`: authenticated loopback API.
- `package.ps1`: portable and source archives; `.github/workflows/release.yml`: tagged GitHub releases.
- `SelfTests.cs`, `tests/integration.ps1`: isolated and media/hardware validation.
