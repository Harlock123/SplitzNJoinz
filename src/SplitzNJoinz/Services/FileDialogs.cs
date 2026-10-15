using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace SplitzNJoinz.Services;

/// <summary>The file and folder pickers, as local paths (null when cancelled).</summary>
public interface IFileDialogs
{
    Task<string?> PickFileAsync(string title, string? startFolder);
    Task<string?> PickFolderAsync(string title, string? startFolder);
    Task<string?> PickSaveFileAsync(string title, string? startFolder, string? suggestedName);
}

public sealed class FileDialogs(TopLevel owner) : IFileDialogs
{
    private IStorageProvider Storage => owner.StorageProvider;

    public async Task<string?> PickFileAsync(string title, string? startFolder)
    {
        var files = await Storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = await FolderAsync(startFolder),
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickFolderAsync(string title, string? startFolder)
    {
        var folders = await Storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = await FolderAsync(startFolder),
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveFileAsync(string title, string? startFolder, string? suggestedName)
    {
        var file = await Storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedStartLocation = await FolderAsync(startFolder),
            SuggestedFileName = suggestedName,
            ShowOverwritePrompt = false, // (the window's own "overwrite" box decides)
        });
        return file?.TryGetLocalPath();
    }

    private async Task<IStorageFolder?> FolderAsync(string? path) =>
        string.IsNullOrWhiteSpace(path) || !Directory.Exists(path) ? null : await Storage.TryGetFolderFromPathAsync(path);
}
