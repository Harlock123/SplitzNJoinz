# Splitz_n_Joinz

A small cross-platform desktop tool (Avalonia, .NET 10) that splits a file into numbered chunks
of a chosen maximum size, and joins the chunks back into the original.

## Splitz

1. **Browse…** for the file to split.
2. Set the **maximum chunk size** (bytes, KB, MB or GB; 1 KB = 1024 bytes).
3. The chunks go beside the file unless you choose another folder.
4. **Split.** `movie.mkv` becomes `movie.mkv.001`, `movie.mkv.002`, … (four or more digits past 999 chunks).

The source file is never changed. A split stops at 10,000 chunks, and won't mix with chunks of the same
file already in the folder unless you tick *Replace chunks* (which removes all of the old ones first).
Cancelling or a failure removes the chunks written so far.

## Joinz

1. **Browse…** for any one of the chunks; the others are found beside it, and any missing numbers are listed.
2. The joined file goes beside the chunks under its original name, unless you choose another.
3. **Join.** The file is built under a temporary name and only moved into place when complete,
   and an existing file is only replaced if you tick *Overwrite*.

## Build and run

```sh
dotnet run --project src/SplitzNJoinz
dotnet test
```

Publish a self-contained build for any desktop platform:

```sh
dotnet publish src/SplitzNJoinz -c Release -r win-x64     --self-contained
dotnet publish src/SplitzNJoinz -c Release -r osx-arm64   --self-contained
dotnet publish src/SplitzNJoinz -c Release -r linux-x64   --self-contained
```

(Also `win-arm64`, `osx-x64` and `linux-arm64`.)

## Layout

| Project | |
|---|---|
| `src/SplitzNJoinz.Core` | Splitting, joining and chunk naming; no UI |
| `src/SplitzNJoinz` | The Avalonia app (`Splitz_n_Joinz` executable) |
| `tests/SplitzNJoinz.Tests` | Round trips and edge cases for the core |
| `tests/SplitzNJoinz.UiTests` | The window and view model, headless |
