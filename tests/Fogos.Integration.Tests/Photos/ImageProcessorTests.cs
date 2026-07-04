using System.Security.Cryptography;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Images;
using SixLabors.ImageSharp;

namespace Fogos.Integration.Tests.Photos;

/// <summary>Image pipeline unit tests (no containers): eXIf walker, DMS→decimal, timezone, resize, stripping, signature.</summary>
public sealed class ImageProcessorTests
{
    private readonly ImageProcessor _processor = new(new FogosClock());

    private static Task<ProcessedPhoto> Process(ImageProcessor processor, byte[] bytes) =>
        processor.ProcessAsync(new MemoryStream(bytes));

    // ── PNG eXIf chunk walker ──────────────────────────────────────────────────

    [Fact]
    public void Walker_finds_spliced_eXIf_chunk_and_returns_its_payload()
    {
        var exif = PhotoFixtures.ExifTiffBytes();
        var png = PhotoFixtures.PngWithSplicedExifChunk(exif);

        Assert.True(PngExifExtractor.TryFindExifChunk(png, out var found));
        Assert.Equal(exif, found);
    }

    [Fact]
    public void Walker_returns_false_for_png_without_eXIf_and_for_non_png()
    {
        var plain = PhotoFixtures.Png(exif: null);
        Assert.False(PngExifExtractor.TryFindExifChunk(plain, out _));

        Assert.False(PngExifExtractor.TryFindExifChunk("not a png at all"u8.ToArray(), out _));
        Assert.False(PngExifExtractor.TryFindExifChunk([], out _));
    }

    [Fact]
    public void Walker_stops_on_truncated_chunk_instead_of_overrunning()
    {
        var exif = PhotoFixtures.ExifTiffBytes();
        var png = PhotoFixtures.PngWithSplicedExifChunk(exif);
        var truncated = png[..(8 + 12)]; // signature + IHDR header only
        Assert.False(PngExifExtractor.TryFindExifChunk(truncated, out _));
    }

    [Fact]
    public async Task Png_with_spliced_eXIf_chunk_flows_through_the_full_pipeline()
    {
        var png = PhotoFixtures.PngWithSplicedExifChunk(PhotoFixtures.ExifTiffBytes(), width: 40, height: 30);

        var result = await Process(_processor, png);

        Assert.Equal(PhotoFixtures.Lat, result.Gps.Latitude, precision: 6);
        Assert.Equal(PhotoFixtures.Lng, result.Gps.Longitude, precision: 6);
        Assert.Equal(123, result.Altitude!.Value, precision: 6);
        Assert.Equal(180, result.Heading!.Value, precision: 6);
    }

    // ── DMS → decimal ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Dms_north_west_yields_positive_lat_negative_lng()
    {
        var jpeg = PhotoFixtures.Jpeg(PhotoFixtures.GpsProfile(latRef: "N", lngRef: "W"));

        var result = await Process(_processor, jpeg);

        Assert.Equal(40 + 30 / 60.0 + 15 / 3600.0, result.Gps.Latitude, precision: 6);
        Assert.Equal(-(8 + 10 / 60.0), result.Gps.Longitude, precision: 6);
    }

    [Fact]
    public async Task Dms_south_east_yields_negative_lat_positive_lng()
    {
        var jpeg = PhotoFixtures.Jpeg(PhotoFixtures.GpsProfile(latRef: "S", lngRef: "E"));

        var result = await Process(_processor, jpeg);

        Assert.Equal(-(40 + 30 / 60.0 + 15 / 3600.0), result.Gps.Latitude, precision: 6);
        Assert.Equal(8 + 10 / 60.0, result.Gps.Longitude, precision: 6);
    }

    [Fact]
    public async Task Missing_gps_throws_MissingGpsException()
    {
        var jpeg = PhotoFixtures.Jpeg(exif: null);
        await Assert.ThrowsAsync<MissingGpsException>(() => Process(_processor, jpeg));
    }

    [Fact]
    public async Task Non_image_bytes_throw_UnsupportedImageFormatException()
    {
        await Assert.ThrowsAsync<UnsupportedImageFormatException>(
            () => Process(_processor, "this is definitely not an image"u8.ToArray()));
    }

    [Fact]
    public async Task Corrupt_jpeg_throws_UndecodableImageException()
    {
        // Valid JPEG magic, garbage body.
        var bytes = new byte[64];
        bytes[0] = 0xFF; bytes[1] = 0xD8; bytes[2] = 0xFF;
        await Assert.ThrowsAsync<UndecodableImageException>(() => Process(_processor, bytes));
    }

    // ── TakenAt: Lisbon → UTC ──────────────────────────────────────────────────

    [Fact]
    public async Task TakenAt_is_interpreted_lisbon_and_converted_to_utc()
    {
        // July → WEST (UTC+1): 14:30 Lisbon == 13:30 UTC.
        var summer = PhotoFixtures.Jpeg(PhotoFixtures.GpsProfile(dateTimeOriginal: "2026:07:15 14:30:00"));
        var result = await Process(_processor, summer);
        Assert.Equal(new DateTimeOffset(2026, 7, 15, 13, 30, 0, TimeSpan.Zero), result.TakenAt);

        // January → WET (UTC+0): 14:30 Lisbon == 14:30 UTC.
        var winter = PhotoFixtures.Jpeg(PhotoFixtures.GpsProfile(dateTimeOriginal: "2026:01:15 14:30:00"), seed: 7);
        var winterResult = await Process(_processor, winter);
        Assert.Equal(new DateTimeOffset(2026, 1, 15, 14, 30, 0, TimeSpan.Zero), winterResult.TakenAt);
    }

    [Fact]
    public async Task Unparseable_DateTimeOriginal_yields_null_TakenAt()
    {
        var jpeg = PhotoFixtures.Jpeg(PhotoFixtures.GpsProfile(dateTimeOriginal: "not a timestamp"));
        var result = await Process(_processor, jpeg);
        Assert.Null(result.TakenAt);
    }

    // ── Resize ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Oversized_image_is_scaled_to_2560_longest_edge()
    {
        var jpeg = PhotoFixtures.Jpeg(PhotoFixtures.GpsProfile(), width: 3000, height: 2000);

        var result = await Process(_processor, jpeg);

        Assert.Equal(2560, result.Width);
        Assert.InRange(result.Height, 1706, 1707); // 2000 * 2560/3000, rounding either way
        using var reloaded = Image.Load(result.JpegBytes);
        Assert.Equal(result.Width, reloaded.Width);
        Assert.Equal(result.Height, reloaded.Height);
    }

    [Fact]
    public async Task Portrait_oversized_image_scales_its_height_edge()
    {
        var jpeg = PhotoFixtures.Jpeg(PhotoFixtures.GpsProfile(), width: 2000, height: 3000);
        var result = await Process(_processor, jpeg);
        Assert.Equal(2560, result.Height);
        Assert.InRange(result.Width, 1706, 1707);
    }

    [Fact]
    public async Task Small_image_is_never_upscaled()
    {
        var jpeg = PhotoFixtures.Jpeg(PhotoFixtures.GpsProfile(), width: 800, height: 600);

        var result = await Process(_processor, jpeg);

        Assert.Equal(800, result.Width);
        Assert.Equal(600, result.Height);
    }

    // ── Metadata stripping ────────────────────────────────────────────────────

    [Fact]
    public async Task Output_jpeg_has_no_exif_profile()
    {
        var jpeg = PhotoFixtures.Jpeg(PhotoFixtures.GpsProfile());

        var result = await Process(_processor, jpeg);

        using var reloaded = Image.Load(result.JpegBytes);
        Assert.Null(reloaded.Metadata.ExifProfile);
        Assert.Null(reloaded.Metadata.IptcProfile);
        Assert.Null(reloaded.Metadata.XmpProfile);
    }

    // ── Signature ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Signature_is_sha256_of_original_bytes_and_stable()
    {
        var jpeg = PhotoFixtures.Jpeg(PhotoFixtures.GpsProfile());

        var first = await Process(_processor, jpeg);
        var second = await Process(_processor, jpeg);

        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(jpeg)), first.Signature);
        Assert.Equal(first.Signature, second.Signature);

        var other = PhotoFixtures.Jpeg(PhotoFixtures.GpsProfile(), seed: 42);
        var third = await Process(_processor, other);
        Assert.NotEqual(first.Signature, third.Signature);
    }
}
