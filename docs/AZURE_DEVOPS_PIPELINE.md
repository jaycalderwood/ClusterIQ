# Azure DevOps Pipeline Guide

## What this package includes

- `azure-pipelines.yml`
- `VERSION.txt`
- `scripts/build-package-selfcontained.ps1`

## What the pipeline does

1. Installs .NET 8 SDK
2. Reads `VERSION.txt`
3. Uses a Git tag such as `v1.0.1` when the build was triggered from a tag
4. Uses a CI version such as `1.0.1-ci.<BuildId>` for non-tag builds
5. Restores and builds the WPF project
6. Publishes a **self-contained single-file win-x64** build
7. Produces:
   - `ClusterIQ_<version>_win-x64.exe`
   - `ClusterIQ_<version>_win-x64.zip`
   - `release-metadata.txt` with SHA256 hashes
8. Publishes those artifacts to Azure DevOps
9. For tag builds, creates a GitHub release and uploads the EXE, ZIP, and metadata file

## Azure DevOps setup

Create a YAML pipeline that points to:

`azure-pipelines.yml`

You also need a GitHub service connection in Azure DevOps.

Recommended service connection name:

`ClusterIQ-GitHub`

The YAML references:

- repository: `jaycalderwood/ClusterIQ`
- service connection: `ClusterIQ-GitHub`

## Tagging for production release

Push a tag such as:

```powershell
git tag v1.0.1
git push origin v1.0.1
```

That tag build will:

- stamp the app version
- produce the self-contained EXE
- upload release assets to GitHub
- create the release as latest

## Why this supports the app updater

The updater checks GitHub Releases. This pipeline uploads release assets with `ClusterIQ` in the filename, which aligns with the updater’s asset discovery logic.

## Notes

- The pipeline uses `windows-latest`
- The publish step uses `--self-contained true`
- The publish step uses `PublishSingleFile=true`
- If you later want code signing, add a signing step after publish and before packaging
