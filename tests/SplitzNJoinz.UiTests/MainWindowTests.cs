using Avalonia.Headless.XUnit;
using SplitzNJoinz.Services;
using SplitzNJoinz.ViewModels;

namespace SplitzNJoinz.UiTests;

public sealed class MainWindowTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("splitz-ui-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>Stands in for the pickers: each returns whatever the test put in the queue.</summary>
    private sealed class FakeDialogs : IFileDialogs
    {
        public Queue<string?> Answers { get; } = new();
        public Task<string?> PickFileAsync(string title, string? startFolder) => Task.FromResult(Answers.Dequeue());
        public Task<string?> PickFolderAsync(string title, string? startFolder) => Task.FromResult(Answers.Dequeue());
        public Task<string?> PickSaveFileAsync(string title, string? startFolder, string? suggestedName) => Task.FromResult(Answers.Dequeue());
    }

    private (MainWindow Window, MainWindowViewModel Vm, FakeDialogs Dialogs) Open()
    {
        var dialogs = new FakeDialogs();
        var vm = new MainWindowViewModel(dialogs);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        return (window, vm, dialogs);
    }

    [AvaloniaFact]
    public async Task Split_then_join_through_the_window_rebuilds_the_file()
    {
        var (_, vm, dialogs) = Open();
        var source = Path.Combine(_dir, "photo.raw");
        var bytes = new byte[2_500_000];
        new Random(7).NextBytes(bytes);
        File.WriteAllBytes(source, bytes);

        dialogs.Answers.Enqueue(source);
        await vm.BrowseSourceCommand.ExecuteAsync(null);
        Assert.Equal(_dir, vm.OutputFolder); // (chunks default to beside the source)

        vm.ChunkSize = 1;
        vm.ChunkUnit = MainWindowViewModel.Units.Single(u => u.Name == "MB");
        Assert.Contains("3 chunks", vm.SplitSummary);
        Assert.True(vm.SplitCommand.CanExecute(null));

        await vm.SplitCommand.ExecuteAsync(null);
        Assert.False(vm.SplitFailed, vm.SplitStatus);
        Assert.StartsWith("Done: 3 chunk(s)", vm.SplitStatus);
        Assert.Equal(100, vm.SplitPercent);

        dialogs.Answers.Enqueue(Path.Combine(_dir, "photo.raw.002"));
        await vm.BrowseChunkCommand.ExecuteAsync(null);
        Assert.False(vm.JoinSummaryIsError, vm.JoinSummary);
        Assert.Contains("all 3 chunk(s) of photo.raw", vm.JoinSummary);
        Assert.Equal(source, vm.JoinOutputPath);
        Assert.True(vm.JoinOutputExists); // (the original is still there)

        // Without "overwrite", the existing original is left alone...
        await vm.JoinCommand.ExecuteAsync(null);
        Assert.True(vm.JoinFailed);

        // ...so save under another name instead.
        var rebuilt = Path.Combine(_dir, "rebuilt.raw");
        dialogs.Answers.Enqueue(rebuilt);
        await vm.BrowseJoinOutputCommand.ExecuteAsync(null);
        Assert.False(vm.JoinOutputExists);
        await vm.JoinCommand.ExecuteAsync(null);
        Assert.False(vm.JoinFailed, vm.JoinStatus);
        Assert.Equal(bytes, File.ReadAllBytes(rebuilt));
    }

    [AvaloniaFact]
    public void A_missing_chunk_is_shown_and_join_stays_disabled()
    {
        var (_, vm, _) = Open();
        File.WriteAllText(Path.Combine(_dir, "notes.txt.001"), "a");
        File.WriteAllText(Path.Combine(_dir, "notes.txt.003"), "c");

        vm.ChunkPath = Path.Combine(_dir, "notes.txt.001");

        Assert.True(vm.JoinSummaryIsError);
        Assert.Contains("Chunk(s) 2 of notes.txt are missing", vm.JoinSummary);
        Assert.False(vm.JoinCommand.CanExecute(null));

        File.WriteAllText(Path.Combine(_dir, "notes.txt.002"), "b");
        vm.RefreshChunksCommand.Execute(null);
        Assert.False(vm.JoinSummaryIsError, vm.JoinSummary);
        Assert.True(vm.JoinCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void A_zero_chunk_size_cannot_split()
    {
        var (_, vm, _) = Open();
        vm.SourcePath = typeof(MainWindowTests).Assembly.Location;
        vm.ChunkSize = 0;
        Assert.False(vm.SplitCommand.CanExecute(null));
        Assert.Contains("at least 1 byte", vm.SplitSummary);
    }
}
