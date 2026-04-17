# ClusterIQ

ClusterIQ is a WPF-based management console for Hyper-V clusters and Azure Local environments. It provides live inventory, VM lifecycle operations, cluster visibility, storage insight, export capability, and GitHub-based app update support.

## Highlights

- VM inventory and lifecycle actions
- Live migration with destination host selection
- Host, cluster, switch, NIC, disk, snapshot, storage, and S2D views
- Current-tab export to CSV and XLSX
- Saved profiles and preferences
- Dark mode
- Silent GitHub release check at startup
- Manual update workflow from the About window

## Build

```powershell
dotnet restore
dotnet build -c Release
```

## Release

```powershell
dotnet publish .\HVTools\HVTools.csproj -c Release -r win-x64 --self-contained false
```

## GitHub Releases and App Update

The application checks this repository for releases:

`https://github.com/jaycalderwood/ClusterIQ`

To make the in-app updater work reliably:

- create a GitHub Release with a semantic version tag such as `v1.0.1`
- attach a release asset containing `ClusterIQ` in the filename
- prefer a `.zip` release artifact for full app replacement

## Live Migration Authentication

Supported modes:

- Kerberos
- CredSSP

All participating hosts should use the same Hyper-V live migration authentication type.

## Repository Layout

- `HVTools/` application source
- `docs/` operator documentation
- `.github/workflows/` GitHub Actions build workflow

## Notes

- Live migration is asynchronous
- Inventory refresh can briefly lag cluster state
- Storage details depend on cmdlet support on the queried node

## Azure DevOps Release Flow

This package includes a self-contained Azure DevOps pipeline that can:

- build a self-contained single-file `win-x64` EXE
- stamp version information from `VERSION.txt` and Git tags
- publish Azure DevOps artifacts
- create a GitHub release and upload the EXE, ZIP, and SHA256 metadata for the app updater

See `docs/AZURE_DEVOPS_PIPELINE.md` for setup details.
