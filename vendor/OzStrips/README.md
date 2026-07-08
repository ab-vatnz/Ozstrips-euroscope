# OzStrips VATNZ

VATNZ/New Zealand build of OzStrips for vaSys.

This branch keeps the VATNZ-specific layout, autofill, server options, and NZ procedure handling on top of upstream OzStrips.

## Install

Download the latest `OzStrips-VATNZ-*.zip` release and extract it to your New Zealand vaSys profile plugins folder.

Example file structure:

```text
C:\Users\<YourName>\Documents\vatSys Files\Profiles\New Zealand\Plugins\OzStrips\
  GUI.dll
  GUI.Shared.dll
  GUI.Connector.dll
  AerodromeSettings.xml
  Strip.xml
  AutoFill\
    NZAA.yml
  x86\
    libSkiaSharp.dll
```

Then start vaSys with the New Zealand profile. OzStrips should load as a normal vaSys plugin.

If you use a different profile location, keep the same `Plugins\OzStrips` folder structure inside that profile.

## Updating From Upstream

See [VATNZ_SYNC.md](VATNZ_SYNC.md).

Short version:

1. Run the **Sync upstream OzStrips** workflow in GitHub Actions.
2. Review and merge the PR into `vatnz/main`.
3. Run the EuroScope repo's **Sync VATNZ OzStrips vendor** workflow.

## Running GitHub Workflows

1. Open the repo on GitHub.
2. Go to **Actions**.
3. Pick the workflow, for example **Sync upstream OzStrips**.
4. Click **Run workflow**.
5. Choose the branch, usually `vatnz/main`.
6. Click the green **Run workflow** button.

The workflow will open a PR when there is something to update.

## Releasing

To create a release, push a version tag from `vatnz/main`:

```powershell
git checkout vatnz/main
git pull
git tag v1.0.0
git push origin v1.0.0
```

The GitHub Actions build will create the release zip and attach it to the GitHub Release.
