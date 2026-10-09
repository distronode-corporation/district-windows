using DistrictAI.Core.Ffi;
using Xunit;

namespace DistrictAI.Core.Tests;

/// <summary>
/// The platform services the areas share (picked files, the brand palette),
/// through the real district-ffi library: what C# reads is what Rust decided.
/// </summary>
public sealed class PlatformServicesTests
{
    private static readonly byte[] _png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];
    private static readonly byte[] _heic = [0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'h', (byte)'e', (byte)'i', (byte)'c'];

    [Fact]
    public void TheTypeComesFromTheBytes()
    {
        Assert.Equal(FileKind.Png, DistrictFfi.FileKind(_png));
        Assert.Equal(FileKind.Heic, DistrictFfi.FileKind(_heic));
        Assert.Equal(FileKind.Unknown, DistrictFfi.FileKind([]));
        Assert.Equal("image/heic", DistrictFfi.FileKindMimeType(FileKind.Heic));
    }

    [Fact]
    public void TheCoreRefusesWhatTheServiceWouldNot()
    {
        Assert.Null(DistrictFfi.AttachmentProblem(new PickedFileView("roof.jpg", 12, _png), 0));
        Assert.Equal(
            "Only JPEG, PNG, GIF or WebP images can be attached.",
            DistrictFfi.AttachmentProblem(new PickedFileView("IMG_0001.jpg", 12, _heic), 0));
    }

    [Fact]
    public void ThePicksNameTheirFilters()
    {
        var attachments = DistrictFfi.AttachmentPick();
        Assert.Equal([".jpg", ".jpeg", ".png", ".gif", ".webp"], attachments.Extensions);
        Assert.Equal(5u, attachments.MaxFiles);
        var logo = DistrictFfi.LogoPick();
        Assert.Equal([".png", ".jpg", ".jpeg", ".webp"], logo.Extensions);
        Assert.Equal(1u, logo.MaxFiles);
    }

    [Fact]
    public async Task AFileIsReadWhole()
    {
        var pick = new FilePickView([".png"], 1, 1024);
        using var stream = new MemoryStream(_png);
        var file = await PickedFiles.ReadAsync("roof.png", 12, stream, pick, TestContext.Current.CancellationToken);
        Assert.Equal("roof.png", file.FileName);
        Assert.Equal(12ul, file.Size);
        Assert.Equal(_png, file.Bytes);
    }

    [Fact]
    public async Task AFileIsReadNoFurtherThanTheCap()
    {
        var pick = new FilePickView([".png"], 1, (ulong)PickedFiles.Chunk + 5);
        var large = new byte[(3 * PickedFiles.Chunk) + 7];
        large[PickedFiles.Chunk + 4] = 1;
        using var stream = new TrickleStream(large);
        var file = await PickedFiles.ReadAsync("large.png", (ulong)large.Length, stream, pick, TestContext.Current.CancellationToken);
        Assert.Equal(PickedFiles.Chunk + 5, file.Bytes.Length);
        Assert.Equal(1, file.Bytes[PickedFiles.Chunk + 4]);
        Assert.Equal((ulong)large.Length, file.Size);
    }

    [Fact]
    public async Task AnEmptyFileIsEmpty()
    {
        var pick = DistrictFfi.AttachmentPick();
        using var stream = new MemoryStream();
        // Windows may report a size the file no longer has.
        var file = await PickedFiles.ReadAsync("empty.png", 10, stream, pick, TestContext.Current.CancellationToken);
        Assert.Empty(file.Bytes);
        Assert.NotNull(DistrictFfi.AttachmentProblem(file, 0));
    }

    [Fact]
    public async Task AFailedReadIsTheCallers()
    {
        var pick = DistrictFfi.AttachmentPick();
        using var stream = new FailingStream();
        await Assert.ThrowsAsync<IOException>(() => PickedFiles.ReadAsync("gone.png", 10, stream, pick, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ThePaletteIsTheCoresInBothThemes()
    {
        var light = DistrictFfi.BrandPalette(false);
        var dark = DistrictFfi.BrandPalette(true);
        Assert.Equal(0xFF01657Du, light.Accent);
        Assert.Equal(0xFFFFFFFFu, light.OnAccent);
        Assert.Equal(0xFF67CDEDu, dark.Accent);
        Assert.Equal(0xFF0E1C1Fu, dark.OnAccent);
    }

    /// <summary>A stream that hands back fewer bytes than asked, as a slow disk can.</summary>
    private sealed class TrickleStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, 1000)], cancellationToken);
    }

    /// <summary>A file that went away while it was read.</summary>
    private sealed class FailingStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("gone"));
    }
}
