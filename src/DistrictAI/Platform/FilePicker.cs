using System.Runtime.InteropServices;
using DistrictAI.Core;
using DistrictAI.Core.Ffi;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace DistrictAI.Platform;

/// <summary>
/// The Windows file chooser over the app's window, for a pick district-ffi
/// describes (<see cref="DistrictFfi.AttachmentPick"/>,
/// <see cref="DistrictFfi.LogoPick"/>): the extensions it offers, how many
/// files, and how much of each is read.
/// </summary>
/// <remarks>
/// A desktop app's picker has no window of its own to belong to, so it is
/// initialised with the window's handle. Call from the UI thread. The type of
/// each file is not decided here: district-ffi sniffs it from the bytes.
/// </remarks>
/// <param name="window">The handle of the window the chooser opens over.</param>
public sealed class FilePicker(nint window)
{
    /// <summary>
    /// Opens the chooser and reads each file picked. Empty when the member
    /// cancelled. A file that could not be read is in the list with no data,
    /// for the area to report as the core's own "could not be read" event.
    /// </summary>
    public async Task<IReadOnlyList<PickedFile>> PickAsync(FilePickView pick, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pick);
        if (pick.Extensions.Length == 0)
        {
            throw new ArgumentException("A pick offers at least one file type.", nameof(pick));
        }
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.Thumbnail,
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
        };
        foreach (var extension in pick.Extensions)
        {
            picker.FileTypeFilter.Add(extension);
        }
        WinRT.Interop.InitializeWithWindow.Initialize(picker, window);

        IReadOnlyList<StorageFile> files;
        if (pick.MaxFiles > 1)
        {
            files = await picker.PickMultipleFilesAsync() ?? [];
        }
        else
        {
            var file = await picker.PickSingleFileAsync();
            files = file is null ? [] : [file];
        }

        var picked = new List<PickedFile>(files.Count);
        foreach (var file in files)
        {
            picked.Add(await ReadAsync(file, pick, cancellationToken).ConfigureAwait(true));
        }
        return picked;
    }

    /// <summary>One file, read up to the pick's cap, or without data when it could not be read.</summary>
    private static async Task<PickedFile> ReadAsync(StorageFile file, FilePickView pick, CancellationToken cancellationToken)
    {
        try
        {
            var properties = await file.GetBasicPropertiesAsync();
            using var stream = await file.OpenStreamForReadAsync().ConfigureAwait(true);
            var data = await PickedFiles.ReadAsync(file.Name, properties.Size, stream, pick, cancellationToken).ConfigureAwait(true);
            return new PickedFile(file.Name, data);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or COMException)
        {
            // Gone, locked, offline or refused since it was picked.
            return new PickedFile(file.Name, null);
        }
    }
}
