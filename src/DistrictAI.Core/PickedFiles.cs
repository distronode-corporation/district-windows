using DistrictAI.Core.Ffi;

namespace DistrictAI.Core;

/// <summary>
/// One file the member picked: its data for the core, or <see langword="null"/>
/// when it could not be read at all. The area turns a readable one into its
/// action with the core's attachment, and an unreadable one into the core's
/// own "could not be read" event (<c>AttachFailed</c>, <c>LogoUnreadable</c>).
/// </summary>
/// <param name="FileName">The file's name, without its folder.</param>
/// <param name="Data">What was read, or <see langword="null"/> when nothing could be.</param>
public sealed record PickedFile(string FileName, PickedFileView? Data);

/// <summary>
/// Reads a picked file into the <see cref="PickedFileView"/> district-ffi
/// takes: from its start, never past the pick's <see cref="FilePickView.ReadCap"/>,
/// which is one byte past the largest file the service accepts, so the core
/// sees that a larger file is too large without the app reading all of it.
/// </summary>
public static class PickedFiles
{
    /// <summary>How much of a file is read at a time.</summary>
    public const int Chunk = 64 * 1024;

    /// <summary>
    /// Reads <paramref name="stream"/>, the file <paramref name="fileName"/> of
    /// <paramref name="size"/> bytes as Windows reported it, up to
    /// <paramref name="pick"/>'s cap. A failure to read is the caller's to turn
    /// into an unreadable <see cref="PickedFile"/>; the stream stays the caller's.
    /// </summary>
    public static async Task<PickedFileView> ReadAsync(
        string fileName,
        ulong size,
        Stream stream,
        FilePickView pick,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(pick);
        var cap = (int)Math.Min(pick.ReadCap, (ulong)Array.MaxLength);
        using var bytes = new MemoryStream((int)Math.Min(size, (ulong)cap));
        var buffer = new byte[Chunk];
        while (bytes.Length < cap)
        {
            var want = (int)Math.Min(Chunk, cap - bytes.Length);
            var read = await stream.ReadAsync(buffer.AsMemory(0, want), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            bytes.Write(buffer, 0, read);
        }
        return new PickedFileView(fileName, size, bytes.ToArray());
    }
}
