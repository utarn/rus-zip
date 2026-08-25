# CLAUDE.md

## Agent skills

### Issue tracker

GitLab issues via `glab`. See `docs/agents/issue-tracker.md`.

### Triage labels

Canonical five-role vocabulary. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context (`CONTEXT.md` + `docs/adr/`). See `docs/agents/domain.md`.

## Deployment & Distribution

### Hybrid Deployment Architecture
- **CLI (`RusZip.Cli`)**: Standard JIT (`PublishReadyToRun=false`) for lean single-file binary size (~74MB).
- **Desktop (`RusZip.Desktop`)**: ReadyToRun AOT (`PublishReadyToRun=true`) for instant cold startup and fast window activation.
- See `docs/adr/0019-hybrid-deployment-cli-jit-desktop-readytorun.md`.

### Versioning & Version Bumps
- Central version defined in `VERSION` and `Directory.Build.props`.
- Bump script: `./bump_version.sh [patch|minor|major|<version>]` or `.\bump_version.ps1`.
- CI autoincrements build version to `1.0.<CI_PIPELINE_IID>` (or uses Git tag).

### Package Managers & Distribution
- **Homebrew Tap (`utarn/rus-zip`)**: `Formula/rus-zip.rb` (CLI) and `Casks/rus-zip.rb` (Desktop app) at repo root.
- **WinGet**: `packaging/winget/rus.zip.yaml` (Package ID: `rus.zip`, monikers: `rus.zip`, `rus-zip`, `ruszip`).
- **Linux Packages**: `packaging/flatpak/` (Flatpak) and `packaging/snap/` (Snapcraft).
- **CI/CD Pipeline**: `.gitlab-ci.yml` builds Linux, Windows, macOS ARM64/x64, computes SHA-256 hashes, publishes GitHub releases to `utarn/rus-zip`, and sends LINE notifications.

