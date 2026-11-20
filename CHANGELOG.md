# Changelog

All notable changes to this package are documented here.

## [1.0.0] - 2026-11-11

### Added

* `IExternalCodeEditor` implementation so Zed appears in **External Tools** and handles *Open C# Project*.
* Zed discovery for Windows (`%LOCALAPPDATA%\Programs\Zed`, `Program Files`, WinGet/Scoop shims, `PATH`),
  macOS (`/Applications/Zed.app` → `Contents/MacOS/cli`, Homebrew) and Linux (`/usr/bin`, `~/.local/bin`, snap, flatpak).
* Opening scripts at `path:line:column`, opening the project folder first so the language server starts with the right workspace root.
* SDK style `.sln` / `.csproj` generation from `CompilationPipeline`, including defines, unsafe blocks,
  language version, assembly references, project references, Unity analyzers and `csc.rsp` arguments.
* Incremental regeneration on script add/delete/move and `.asmdef` / `.dll` changes.
* Optional Unity friendly `.zed/settings.json` with `file_scan_exclusions`.
* `Tools/Zed` menu entries and `ZedCommandLine` batch mode entry points.
* Support for `AssetPostprocessor.OnGeneratedCSProject` / `OnGeneratedSlnSolution` hooks.
