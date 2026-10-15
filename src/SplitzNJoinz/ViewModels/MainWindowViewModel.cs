using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SplitzNJoinz.Core;
using SplitzNJoinz.Services;

namespace SplitzNJoinz.ViewModels;

public sealed record SizeUnit(string Name, long Bytes)
{
    public override string ToString() => Name;
}

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IFileDialogs _dialogs;

    public MainWindowViewModel(IFileDialogs dialogs) => _dialogs = dialogs;

    public static IReadOnlyList<SizeUnit> Units { get; } =
    [
        new("bytes", 1),
        new("KB", 1L << 10),
        new("MB", 1L << 20),
        new("GB", 1L << 30),
    ];

    // ---- Split ----

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SplitCommand))]
    public partial string SourcePath { get; set; } = "";

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SplitCommand))]
    public partial string OutputFolder { get; set; } = "";

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SplitCommand))]
    public partial decimal? ChunkSize { get; set; } = 100;

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SplitCommand))]
    public partial SizeUnit ChunkUnit { get; set; } = Units[2];

    [ObservableProperty]
    public partial bool ReplaceExistingChunks { get; set; }

    [ObservableProperty]
    public partial string SplitSummary { get; set; } = "Choose a file to split.";

    [ObservableProperty]
    public partial double SplitPercent { get; set; }

    [ObservableProperty]
    public partial string SplitStatus { get; set; } = "";

    [ObservableProperty]
    public partial bool SplitFailed { get; set; }

    private long SourceBytes => File.Exists(SourcePath) ? new FileInfo(SourcePath).Length : -1;

    /// <summary>The chunk size in whole bytes, or 0 when it isn't a usable size.</summary>
    public long MaxChunkBytes =>
        ChunkSize is { } size && size > 0 && size * ChunkUnit.Bytes < long.MaxValue
            ? (long)decimal.Floor(size * ChunkUnit.Bytes)
            : 0;

    partial void OnSourcePathChanged(string oldValue, string newValue)
    {
        // Chunks go beside the source unless another folder was chosen.
        var oldFolder = string.IsNullOrWhiteSpace(oldValue) ? "" : Path.GetDirectoryName(oldValue) ?? "";
        if (string.IsNullOrWhiteSpace(OutputFolder) || OutputFolder == oldFolder)
            OutputFolder = File.Exists(newValue) ? Path.GetDirectoryName(Path.GetFullPath(newValue)) ?? "" : "";
        UpdateSplitSummary();
    }

    partial void OnChunkSizeChanged(decimal? value) => UpdateSplitSummary();
    partial void OnChunkUnitChanged(SizeUnit value) => UpdateSplitSummary();

    private void UpdateSplitSummary()
    {
        var bytes = SourceBytes;
        var max = MaxChunkBytes;
        if (string.IsNullOrWhiteSpace(SourcePath)) SplitSummary = "Choose a file to split.";
        else if (bytes < 0) SplitSummary = "That file doesn't exist.";
        else if (max <= 0) SplitSummary = $"{Format.Bytes(bytes)}. Enter a chunk size of at least 1 byte.";
        else
        {
            var count = FileSplitter.ChunkCount(bytes, max);
            SplitSummary = count > FileSplitter.MaxChunks
                ? $"{Format.Bytes(bytes)} would make {count:N0} chunks; the limit is {FileSplitter.MaxChunks:N0}."
                : $"{Format.Bytes(bytes)} → {count:N0} chunk{(count == 1 ? "" : "s")} of up to {Format.Bytes(max)}.";
        }
    }

    [RelayCommand]
    private async Task BrowseSourceAsync()
    {
        var path = await _dialogs.PickFileAsync("Choose the file to split", FolderOf(SourcePath));
        if (path is not null) SourcePath = path;
    }

    [RelayCommand]
    private async Task BrowseOutputFolderAsync()
    {
        var path = await _dialogs.PickFolderAsync("Choose where the chunks go", OutputFolder);
        if (path is not null) OutputFolder = path;
    }

    private bool CanSplit() =>
        File.Exists(SourcePath) && !string.IsNullOrWhiteSpace(OutputFolder) && MaxChunkBytes > 0;

    [RelayCommand(CanExecute = nameof(CanSplit), IncludeCancelCommand = true)]
    private async Task SplitAsync(CancellationToken token)
    {
        SplitFailed = false;
        SplitPercent = 0;
        SplitStatus = "Splitting...";
        var progress = new ThrottledProgress(p =>
        {
            SplitPercent = p.Fraction * 100;
            SplitStatus = $"Writing chunk {p.Chunk} of {p.ChunkCount}  ({Format.Bytes(p.BytesDone)} of {Format.Bytes(p.TotalBytes)})";
        });
        var (source, folder, max, replace) = (SourcePath, OutputFolder, MaxChunkBytes, ReplaceExistingChunks);
        var watch = Stopwatch.StartNew();
        try
        {
            var result = await Task.Run(() => FileSplitter.SplitAsync(source, folder, max, replace, progress, token), token);
            SplitPercent = 100;
            SplitStatus = $"Done: {result.ChunkPaths.Count:N0} chunk(s) written to {folder} in {Format.Elapsed(watch.Elapsed)}.";
        }
        catch (OperationCanceledException)
        {
            SplitStatus = "Cancelled. The chunks written so far were removed.";
        }
        catch (Exception e)
        {
            SplitFailed = true;
            SplitStatus = e.Message;
        }
        finally
        {
            progress.Stop();
        }
    }

    // ---- Join ----

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(JoinCommand))]
    public partial string ChunkPath { get; set; } = "";

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(JoinCommand))]
    public partial string JoinOutputPath { get; set; } = "";

    [ObservableProperty]
    public partial bool OverwriteJoinedFile { get; set; }

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(JoinCommand))]
    public partial ChunkSet? Chunks { get; set; }

    [ObservableProperty]
    public partial string JoinSummary { get; set; } = "Choose any one of the chunk files.";

    [ObservableProperty]
    public partial bool JoinSummaryIsError { get; set; }

    [ObservableProperty]
    public partial double JoinPercent { get; set; }

    [ObservableProperty]
    public partial string JoinStatus { get; set; } = "";

    [ObservableProperty]
    public partial bool JoinFailed { get; set; }

    public bool JoinOutputExists => File.Exists(JoinOutputPath);

    partial void OnChunkPathChanged(string value) => RefreshChunks();

    partial void OnJoinOutputPathChanged(string value) => OnPropertyChanged(nameof(JoinOutputExists));

    /// <summary>Looks again for the chunks beside <see cref="ChunkPath"/> (e.g. after copying in a missing one).</summary>
    [RelayCommand]
    private void RefreshChunks()
    {
        var previousDefault = Chunks?.DefaultOutputPath;
        Chunks = null;
        JoinSummaryIsError = false;
        if (string.IsNullOrWhiteSpace(ChunkPath))
        {
            JoinSummary = "Choose any one of the chunk files.";
            return;
        }
        try
        {
            Chunks = FileJoiner.Discover(ChunkPath);
            JoinSummary = $"Found all {Chunks.ChunkPaths.Count:N0} chunk(s) of {Chunks.OriginalFileName}: {Format.Bytes(Chunks.TotalBytes)} in total.";
            // The joined file goes beside the chunks unless another place was chosen.
            if (string.IsNullOrWhiteSpace(JoinOutputPath) || JoinOutputPath == previousDefault)
                JoinOutputPath = Chunks.DefaultOutputPath;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
        {
            JoinSummary = e.Message;
            JoinSummaryIsError = true;
        }
        OnPropertyChanged(nameof(JoinOutputExists));
    }

    [RelayCommand]
    private async Task BrowseChunkAsync()
    {
        var path = await _dialogs.PickFileAsync("Choose any one of the chunks", FolderOf(ChunkPath));
        if (path is null) return;
        if (path == ChunkPath) RefreshChunks();
        else ChunkPath = path;
    }

    [RelayCommand]
    private async Task BrowseJoinOutputAsync()
    {
        var path = await _dialogs.PickSaveFileAsync("Save the joined file as",
            FolderOf(JoinOutputPath) ?? Chunks?.Directory,
            string.IsNullOrWhiteSpace(JoinOutputPath) ? Chunks?.OriginalFileName : Path.GetFileName(JoinOutputPath));
        if (path is not null) JoinOutputPath = path;
    }

    private bool CanJoin() => Chunks is not null && !string.IsNullOrWhiteSpace(JoinOutputPath);

    [RelayCommand(CanExecute = nameof(CanJoin), IncludeCancelCommand = true)]
    private async Task JoinAsync(CancellationToken token)
    {
        JoinFailed = false;
        JoinPercent = 0;
        JoinStatus = "Joining...";
        var progress = new ThrottledProgress(p =>
        {
            JoinPercent = p.Fraction * 100;
            JoinStatus = $"Reading chunk {p.Chunk} of {p.ChunkCount}  ({Format.Bytes(p.BytesDone)} of {Format.Bytes(p.TotalBytes)})";
        });
        var (output, overwrite) = (JoinOutputPath, OverwriteJoinedFile);
        var watch = Stopwatch.StartNew();
        try
        {
            // (Look again first: a chunk may have been deleted or added since it was chosen.)
            var chunks = FileJoiner.Discover(ChunkPath);
            await Task.Run(() => FileJoiner.JoinAsync(chunks, output, overwrite, progress, token), token);
            JoinPercent = 100;
            JoinStatus = $"Done: {Path.GetFileName(output)} ({Format.Bytes(chunks.TotalBytes)}) rebuilt in {Format.Elapsed(watch.Elapsed)}.";
        }
        catch (OperationCanceledException)
        {
            JoinStatus = "Cancelled. Nothing was written.";
        }
        catch (Exception e)
        {
            JoinFailed = true;
            JoinStatus = e.Message;
        }
        finally
        {
            progress.Stop();
            OnPropertyChanged(nameof(JoinOutputExists));
        }
    }

    private static string? FolderOf(string path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetDirectoryName(Path.GetFullPath(path));

    /// <summary>Passes progress to the UI thread at most every 50 ms, so a fast copy doesn't flood it.</summary>
    private sealed class ThrottledProgress(Action<ChunkProgress> onUiThread) : IProgress<ChunkProgress>
    {
        private readonly Stopwatch _since = Stopwatch.StartNew();
        private volatile bool _stopped;

        public void Report(ChunkProgress value)
        {
            lock (_since)
            {
                if (_since.ElapsedMilliseconds < 50) return;
                _since.Restart();
            }
            Dispatcher.UIThread.Post(() => { if (!_stopped) onUiThread(value); });
        }

        /// <summary>Drops reports still queued, so they can't overwrite the final status.</summary>
        public void Stop() => _stopped = true;
    }
}
