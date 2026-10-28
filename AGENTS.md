# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers what the tool does and how to use it;
this file covers how the code fits together and the rules that keep it working.

## What this is

Splitz_n_Joinz, a small desktop tool in C# on .NET 10 and Avalonia 12 (MVVM with CommunityToolkit.Mvvm). It
splits a file into numbered chunks of a chosen maximum size (`movie.mkv.001`, `.002`, ...) and joins them back.
The executable is named `Splitz_n_Joinz`.

## Commands

```bash
dotnet build SplitzNJoinz.sln -warnaserror     # net10.0 from Directory.Build.props; there is no global.json
dotnet test SplitzNJoinz.sln                   # both test projects run headless: no window opens
dotnet run --project src/SplitzNJoinz          # run the app
```

- Package versions are set centrally in `Directory.Packages.props`; `AvaloniaVersion` sets all the Avalonia
  packages at once.
- CI builds with `-warnaserror`, so keep the build free of warnings.

## Layout

- `src/SplitzNJoinz.Core/`: splitting and joining, with no UI dependency.
  - `ChunkNaming.cs`: chunk file names, digit width (at least three) and finding a file's chunks.
  - `FileSplitter.cs`: `SplitAsync`, with progress, cancellation and the 10,000-chunk limit.
  - `FileJoiner.cs`: `Discover` (find every chunk from any one of them) and `JoinAsync`.
- `src/SplitzNJoinz/`: the Avalonia app.
  - `MainWindow.axaml` and `ViewModels/MainWindowViewModel.cs`: the one window, with Splitz and Joinz parts.
  - `ViewModels/Format.cs`: size and number formatting.
  - `Services/FileDialogs.cs`: `IFileDialogs`, the file and folder pickers.
- `tests/SplitzNJoinz.Tests/`: xUnit 2 round trips and edge cases for the core.
- `tests/SplitzNJoinz.UiTests/`: xUnit v3 with `Avalonia.Headless.XUnit`; drives the window and view model with a
  fake `IFileDialogs`.

## Rules that keep it working

- **Core stays UI-free.** File logic goes in `SplitzNJoinz.Core`, where the plain tests reach it. The view model
  only gathers choices, calls the core and reports progress.
- **Never damage the user's files.**
  - A split never changes the source. On failure or cancel it deletes the chunks it wrote.
  - A split refuses to mix with existing chunks of the same file unless told to replace them, and then removes
    all of them first, so a stale chunk cannot join a later set.
  - A join writes to a temporary name and renames it into place only when complete. It never overwrites an
    existing file unless asked, and never writes over one of its own chunks.
- **File pickers go through `IFileDialogs`**, so the UI tests can replace them.
- **Tests work in temporary folders** (`Directory.CreateTempSubdirectory`) and clean up after themselves.

## Making a change

- Add or update tests with every change: core behaviour in `SplitzNJoinz.Tests`, window behaviour in
  `SplitzNJoinz.UiTests`. `dotnet test` must pass, with no warnings.
- Update `README.md` to match any change in behaviour.

## CI and releases

`.github/workflows/build.yml` builds with `-warnaserror` and runs the tests on every push and pull request, on
.NET 10. It then publishes self-contained single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm,
linux-musl-x64/arm64 and osx-x64/arm64, and keeps them as workflow artifacts. Pushes to `main` replace the assets of
the `latest` pre-release, and a `v*` tag makes a proper release.
