# GitHub Release Guide

## First Push

```powershell
git init
git add .
git commit -m "ClusterIQ v1.0.1"
git branch -M main
git remote add origin https://github.com/jaycalderwood/ClusterIQ.git
git push -u origin main
```

## Create a Release

1. Open the repository on GitHub.
2. Go to **Releases**.
3. Click **Draft a new release**.
4. Use a tag such as `v1.0.1`.
5. Upload the packaged application zip.
6. Publish the release.

## Updater Requirements

The in-app updater checks the latest GitHub release and expects a release asset containing `ClusterIQ` in the filename.
