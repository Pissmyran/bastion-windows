# REPO.md

Bastion for Windows: an SSH client with a cross-platform .NET core and a WinUI app. Sibling repos cover the other platforms.

## Stack
- C#, .NET, WinUI, SSH.NET, xUnit
- Documentation: Zensical
- Automation: GitHub Actions, CodeQL, CodeRabbit, Renovate

## Commands
- Docs: `zensical build --clean --strict`
- Core tests and app build: see `docs/operations.md`. They need the .NET SDK and, for the app, Windows.
- No lint or run command exists.

## Where things live
- `WindowsApp/Assets/xterm/`: vendored terminal frontend. Do not edit by hand.
- `WindowsApp/Bastion.Core.Tests/csharp-hosts-fixture.json`: copy of the sync fixture from the Apple repo.
- `.github/scripts/`: release automation.
- `.github/release-baseline`, `.github/release-required-checks`: release inputs.
- `docs/agents/`: domain and issue-tracker notes for agents.

## Release
- The PR title (Conventional Commits) sets the version. Merging to `main` runs `release.yml`, which tags and publishes a GitHub Release once the required checks are green.
- Documentation is built from the release tag and published to GitHub Pages.
- A release does not distribute the app.

## Repo-specific rules
- Sync format changes are made in the Apple repo, not here.
- CI does not build or test the C# code. Run the core tests locally before every push.
- Tags are never moved. Fix a bad release with a new `fix:` or `revert:` PR.
