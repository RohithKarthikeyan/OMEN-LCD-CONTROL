# GitHub releases

Use this source folder as the repository root. It contains the README, MIT license, contribution guide, issue template, Windows build workflow, and tagged release workflow. Do not commit `dist/`, `release/`, local API tokens, personal settings or HP binaries.

Build locally with `./package.ps1` in Windows PowerShell. This compiles the executable, runs the isolated tests, and creates `release/OmenLCDControl-vVERSION-windows-x64.zip` and the matching source ZIP. The portable package includes the executable, documentation, screenshots and sender example. No installation is required.

Before a new release, update the assembly and manifest versions, README status, and CHANGELOG; add release notes under `docs/releases/`. Build and visually verify the editor and Stats studio. Hardware checks are opt-in and require HP software; CI does not access hardware.

After committing and pushing the source to your GitHub repository, tag the intended commit (for example `v0.3.1`) and push that tag. The release workflow builds and tests on Windows, uploads both ZIPs, and creates a GitHub Release with generated notes. It uses the repository's built-in `GITHUB_TOKEN` with release-job-only contents-write permission; no personal access token is required. Tags and public releases should be created only when you intend to publish that commit. The workflow must be allowed to run in repository settings.

This source was prepared locally without a configured Git remote. No GitHub repository or release has been created by preparing these files.
