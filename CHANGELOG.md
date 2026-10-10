# Changelog

## 0.3.2 — 2026-10-10

- Added Custom video / GIF / picture as a fourth choice alongside Rings, Bars and Spotlight.
- Renamed the editor's Stats studio button to Display modes for clearer navigation.
- The custom media choice opens the existing drag/zoom and Fit/Fill editor; applying media replaces stats playback.
- Updated README, mode guide, screenshots and portable/source packages; verified fourth-option navigation and GIF/video conversion.

## 0.3.1 — 2026-10-10

- Renamed the app, executable, project and release packages to Omen LCD control.
- Updated screenshots, README, release notes and GitHub packaging/release workflows.
- Kept the existing local settings, recovery files, API token and single-instance guard compatible with earlier releases.

## 0.3.0 — 2026-10-10

- Stats studio with configurable Rings, Bars and Spotlight layouts.
- Six assignable slots, graph ranges, colors, enable switches, and JSON design import/export.
- CPU speed, optional LibreHardwareMonitor CPU temperature, NVIDIA GPU usage/temperature/clock/VRAM, and LibreHardwareMonitor GPU fallback.
- Authenticated loopback API for custom numeric/text values, with stale-data expiry and bounded requests.
- API documentation, Python sender example, renderer/API tests, and sensor/dashboard hardware integration checks.
- Stats and publishers continue in the system tray; HP Image-mode refresh remains about 2 Hz.

## 0.2.0 — 2026-10-10

- Portable C# Windows executable with circular animated GIF preview.
- Drag positioning, mouse-wheel/slider zoom, Fit/Fill, exact X/Y, FPS selection.
- Background conversion with cancellation before applying.
- System tray menu, close-to-tray behavior, and single-instance guard.
- Live CPU/RAM/time dashboard continues in the tray.
- Local preferences and recent media.
- Recovery backups, exact restoration, and failed-Apply rollback.
- Build script, isolated tests, media integration tests, opt-in hardware tests, and CI workflow.

## 0.1.0 — 2026-10-03

- Initial PowerShell proof of concept controlling HP's existing LCD helper.
