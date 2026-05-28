# OzStrips EuroScope

OzStrips EuroScope is a EuroScope plugin and helper app that brings the OzStrips stripboard workflow to EuroScope.

EuroScope plugins are native C++ DLLs, while the original OzStrips client is a C# vaSys plugin. This repo bridges that gap with:

- a native EuroScope plugin that reads flight plans, runways, SIDs/STARs, squawks, routes, controller/server state, and sends commands back into EuroScope
- a named-pipe bridge between EuroScope and the C# side
- an adapted OzStrips WinForms helper that renders the stripboard and talks to the normal OzStrips server
- a vaSys compatibility shim so most of the OzStrips GUI code can keep using the same shape of APIs

## Current Status

This is working prototype code for VATNZ/New Zealand testing. It is not a polished upstream release yet.

Known working areas:

- open the stripboard from EuroScope with `.ozstrips`
- show EuroScope aircraft as OzStrips strips
- sync strip state through the OzStrips server, including vatSys/EuroScope mixed use when both clients use the same aerodrome and server bucket
- support VATSIM, Sweatbox, and localhost test server modes
- edit common strip fields through the OzStrips UI and push them back toward EuroScope flight plan state
- show EuroScope SID/STAR options in the custom OzStrips dropdowns
- parse EuroScope-style ATIS runway text for autofill

Still expected to move as the project matures:

- airport-specific autofill data
- broader non-NZ sector-file testing
- packaging and installer shape
- release signing/versioning

## Repository Layout

```text
OzStripsEuroScope.sln
external/euroscope/
  EuroScopePlugIn.h
  EuroScopePlugInDll.lib
scripts/
  Deploy-EuroScope.ps1
src/OzStripsEuroScope.Plugin/
  Native EuroScope plugin DLL
src/OzStripsEuroScope.OzStripsGui/
  Adapted OzStrips GUI/helper app
src/OzStripsEuroScope.VatSysShim/
  vaSys API compatibility layer backed by EuroScope data
src/OzStripsEuroScope.Helper/
  Early standalone helper prototype kept for reference
vendor/OzStrips/
  Upstream OzStrips source snapshot used by the helper projects
```

Generated output goes to `bin`, `obj`, or `dist` and is intentionally ignored by git.

## Requirements

- Windows
- EuroScope installed
- Visual Studio 2026/18 with C++ desktop tools
- .NET SDK capable of building `net481`
- vaSys installed if you want to build against local vaSys-compatible references during development

The native plugin is built as Win32/x86 because EuroScope is a 32-bit application.

## Build

Build the C# helper:

```powershell
dotnet build .\src\OzStripsEuroScope.OzStripsGui\OzStripsEuroScope.OzStripsGui.csproj -c Release
```

Build the native EuroScope plugin:

```powershell
& 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe' `
  .\src\OzStripsEuroScope.Plugin\OzStripsEuroScope.Plugin.vcxproj `
  /p:Configuration=Release `
  /p:Platform=Win32 `
  /m
```

Or build and deploy in one step:

```powershell
.\scripts\Deploy-EuroScope.ps1
```

## Deploy

The deploy script defaults to:

```text
C:\Program Files (x86)\EuroScope\VATNZ-SKYLINE_2412\Plugins\OzStripsEuroScope
```

If PowerShell cannot write to Program Files, it falls back to:

```text
dist\OzStripsEuroScope
```

You can also pass a custom target:

```powershell
.\scripts\Deploy-EuroScope.ps1 -TargetRoot 'C:\Path\To\EuroScope\Plugins\OzStripsEuroScope'
```

Load this DLL in EuroScope:

```text
OzStripsEuroScope.dll
```

The helper executable must be in the same folder as the plugin DLL:

```text
OzStripsEuroScope.Helper.exe
```

## EuroScope Commands

Type these commands in EuroScope:

```text
.ozstrips
.ozstrips open
.ozstrips snapshot
```

`.ozstrips` and `.ozstrips open` launch or focus the helper. `.ozstrips snapshot` sends the current EuroScope traffic/sector snapshot to the helper.

## Server Modes

The bridge keeps the normal OzStrips server buckets:

- `VATSIM`
- `SWEATBOX1`
- `SWEATBOX2`
- `SWEATBOX3`
- `LOCALHOST`

EuroScope Sweatbox connections auto-select `SWEATBOX1`. EuroScope simulator/client/playback style connections auto-select `LOCALHOST`. Plain direct connections do not force `VATSIM`, so manual server selections are left alone.

Use `Settings -> Server -> Localhost FSD` in the OzStrips helper when testing against a local FSD server that EuroScope reports as a normal direct connection.

## Notes For VATNZ Testing

- The included NZAA autofill file is test data, not a complete national dataset.
- EuroScope and vaSys format SIDs/STARs differently. The helper displays a vaSys-style short form where appropriate while keeping EuroScope-format procedure/runway data for EuroScope flight plan writes.
- When another controller is using vaSys OzStrips and you are using EuroScope OzStrips, both clients should sync through OzStrips as long as they are connected to the same aerodrome and server bucket.

## Keeping In Sync With OzStrips

Use `OzStrips-NZ-` as the source of truth for VATNZ vaSys OzStrips changes.

The intended update chain is:

```text
maxrumsey/OzStrips
  -> ab-vatnz/OzStrips-NZ-:vatnz/main
  -> this repo's vendor/OzStrips
  -> manually port needed changes into src/OzStripsEuroScope.OzStripsGui
```

Run the GitHub workflow **Sync VATNZ OzStrips vendor** after `OzStrips-NZ-:vatnz/main` has been updated. It opens a PR that refreshes only `vendor/OzStrips`.

To do the same locally:

```powershell
.\scripts\Sync-FromVatnzOzStrips.ps1
```

Do not blindly copy `vendor/OzStrips/GUI` over `src/OzStripsEuroScope.OzStripsGui`. The EuroScope copy contains bridge-specific adaptations for the named-pipe helper, EuroScope SID/STAR formats, runway handling, and EuroScope flight plan writes.

## Credits

This project is based on OzStrips by Max Rumsey and adapts the OzStrips GUI for use from EuroScope.
