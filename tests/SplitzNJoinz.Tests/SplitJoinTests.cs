using SplitzNJoinz.Core;

namespace SplitzNJoinz.Tests;

public sealed class SplitJoinTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("splitz-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string MakeFile(string name, int bytes, int seed = 1)
    {
        var data = new byte[bytes];
        new Random(seed).NextBytes(data);
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, data);
        return path;
    }

    [Theory]
    [InlineData(0, 10, 1)]
    [InlineData(1, 10, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(2_500_000, 1_000_000, 3)]
    public async Task Split_then_join_gives_back_the_same_bytes(int size, int chunk, int expectedChunks)
    {
        var source = MakeFile("data.bin", size);
        var outDir = Path.Combine(_dir, "chunks");

        var result = await FileSplitter.SplitAsync(source, outDir, chunk);

        Assert.Equal(expectedChunks, result.ChunkPaths.Count);
        Assert.All(result.ChunkPaths, p => Assert.True(new FileInfo(p).Length <= chunk));
        Assert.Equal("data.bin.001", Path.GetFileName(result.ChunkPaths[0]));

        var set = FileJoiner.Discover(result.ChunkPaths[^1]);
        Assert.Equal(expectedChunks, set.ChunkPaths.Count);
        Assert.Equal(Path.Combine(outDir, "data.bin"), set.DefaultOutputPath);

        await FileJoiner.JoinAsync(set, set.DefaultOutputPath);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(set.DefaultOutputPath));
    }

    [Fact]
    public async Task Chunk_numbers_widen_past_999()
    {
        var source = MakeFile("wide.bin", 1200);
        var result = await FileSplitter.SplitAsync(source, _dir, 1);
        Assert.Equal("wide.bin.0001", Path.GetFileName(result.ChunkPaths[0]));
        Assert.Equal("wide.bin.1200", Path.GetFileName(result.ChunkPaths[^1]));
        Assert.Equal(1200, FileJoiner.Discover(result.ChunkPaths[500]).ChunkPaths.Count);
    }

    [Fact]
    public async Task A_missing_chunk_is_reported()
    {
        var source = MakeFile("gap.bin", 50);
        var result = await FileSplitter.SplitAsync(source, _dir, 10);
        File.Delete(result.ChunkPaths[2]);
        File.Delete(result.ChunkPaths[3]);

        var e = Assert.Throws<InvalidDataException>(() => FileJoiner.Discover(result.ChunkPaths[0]));
        Assert.Contains("3-4", e.Message);
    }

    [Fact]
    public async Task Existing_chunks_block_a_split_unless_replaced()
    {
        var source = MakeFile("again.bin", 100);
        await FileSplitter.SplitAsync(source, _dir, 10);
        await Assert.ThrowsAsync<IOException>(() => FileSplitter.SplitAsync(source, _dir, 50));

        // Replacing removes all ten old chunks, not just the two the new split overwrites.
        var result = await FileSplitter.SplitAsync(source, _dir, 50, replaceExisting: true);
        Assert.Equal(2, FileJoiner.Discover(result.ChunkPaths[0]).ChunkPaths.Count);
    }

    [Fact]
    public async Task Join_will_not_overwrite_unless_allowed()
    {
        var source = MakeFile("keep.bin", 30);
        var chunks = Path.Combine(_dir, "c");
        var set = FileJoiner.Discover((await FileSplitter.SplitAsync(source, chunks, 10)).ChunkPaths[0]);
        var existing = MakeFile("existing.bin", 5, seed: 2);

        await Assert.ThrowsAsync<IOException>(() => FileJoiner.JoinAsync(set, existing));
        Assert.Equal(5, new FileInfo(existing).Length);

        await FileJoiner.JoinAsync(set, existing, overwrite: true);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(existing));
    }

    [Fact]
    public async Task Too_many_chunks_is_refused()
    {
        var source = MakeFile("big.bin", FileSplitter.MaxChunks + 1);
        await Assert.ThrowsAsync<ArgumentException>(() => FileSplitter.SplitAsync(source, _dir, 1));
        Assert.Empty(ChunkNaming.FindChunks(_dir, "big.bin"));
    }

    [Fact]
    public void A_file_without_a_number_is_not_a_chunk()
    {
        var path = MakeFile("plain.txt", 3);
        Assert.Throws<ArgumentException>(() => FileJoiner.Discover(path));
    }

    [Theory]
    [InlineData("movie.mkv.007", "movie.mkv", 7)]
    [InlineData("a.1", "a", 1)]
    public void Parses_chunk_names(string name, string original, int number)
    {
        Assert.True(ChunkNaming.TryParse(name, out var o, out var n));
        Assert.Equal((original, number), (o, n));
    }

    [Theory]
    [InlineData("movie.mkv")]
    [InlineData("noext")]
    [InlineData(".001")]
    [InlineData("a.")]
    public void Rejects_non_chunk_names(string name) => Assert.False(ChunkNaming.TryParse(name, out _, out _));
}
