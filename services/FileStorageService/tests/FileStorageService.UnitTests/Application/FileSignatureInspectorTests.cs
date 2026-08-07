using FileStorageService.Application.Validation;

namespace FileStorageService.UnitTests.Application;

public class FileSignatureInspectorTests
{
    [Fact]
    public void Detect_JpegBytes_ReturnsJpeg()
    {
        var signature = FileSignatureInspector.Detect([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46]);

        signature.Should().NotBeNull();
        signature!.Mime.Should().Be("image/jpeg");
        signature.Matches(".jpeg").Should().BeTrue();
        signature.Matches(".jpg").Should().BeTrue();
    }

    [Fact]
    public void Detect_HeicBytes_ReturnsHeif_NotJpeg()
    {
        var signature = FileSignatureInspector.Detect(BuildIsoBaseMedia("heic"));

        signature.Should().NotBeNull();
        signature!.Mime.Should().Be("image/heic");
        signature.Matches(".jpeg").Should().BeFalse();
    }

    [Theory]
    [InlineData("heix")]
    [InlineData("hevc")]
    [InlineData("mif1")]
    [InlineData("msf1")]
    public void Detect_HeifBrandVariants_ReturnHeif(string brand)
    {
        FileSignatureInspector.Detect(BuildIsoBaseMedia(brand))!.Mime.Should().Be("image/heic");
    }

    [Fact]
    public void Detect_AvifBytes_ReturnsAvif()
    {
        FileSignatureInspector.Detect(BuildIsoBaseMedia("avif"))!.Mime.Should().Be("image/avif");
    }

    [Fact]
    public void Detect_M4aBytes_ReturnsIsoMedia_WithAudioMimeForM4a()
    {
        var signature = FileSignatureInspector.Detect(BuildIsoBaseMedia("M4A "));

        signature.Should().NotBeNull();
        signature!.Matches(".m4a").Should().BeTrue();
        signature.MimeFor(".m4a").Should().Be("audio/mp4");
        signature.MimeFor(".mp4").Should().Be("video/mp4");
    }

    [Fact]
    public void Detect_PngBytes_ReturnsPng()
    {
        FileSignatureInspector
            .Detect([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])!
            .Mime.Should().Be("image/png");
    }

    [Fact]
    public void Detect_RiffWebp_ReturnsWebp_AndRiffWave_ReturnsWav()
    {
        var webp = "RIFF"u8.ToArray().Concat(new byte[] { 0, 0, 0, 0 }).Concat("WEBP"u8.ToArray()).ToArray();
        var wav = "RIFF"u8.ToArray().Concat(new byte[] { 0, 0, 0, 0 }).Concat("WAVE"u8.ToArray()).ToArray();

        FileSignatureInspector.Detect(webp)!.Mime.Should().Be("image/webp");
        FileSignatureInspector.Detect(wav)!.Mime.Should().Be("audio/wav");
    }

    [Fact]
    public void Detect_PdfAndZipAndGif_AreRecognised()
    {
        FileSignatureInspector.Detect("%PDF-1.7"u8)!.Mime.Should().Be("application/pdf");
        FileSignatureInspector.Detect([0x50, 0x4B, 0x03, 0x04])!.Matches(".docx").Should().BeTrue();
        FileSignatureInspector.Detect("GIF89a"u8)!.Mime.Should().Be("image/gif");
    }

    [Fact]
    public void Detect_UnknownBytes_ReturnsNull()
    {
        FileSignatureInspector.Detect([0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08]).Should().BeNull();
    }

    [Fact]
    public void Detect_TooShortHeader_ReturnsNull()
    {
        FileSignatureInspector.Detect([0xFF, 0xD8]).Should().BeNull();
    }

    [Theory]
    [InlineData(".txt")]
    [InlineData(".csv")]
    [InlineData(".doc")]
    [InlineData(".xls")]
    [InlineData(".bin")]
    [InlineData(".hex")]
    [InlineData(".fw")]
    public void FindByExtension_ExtensionsWithoutStableMagicBytes_ReturnNull(string extension)
    {
        FileSignatureInspector.FindByExtension(extension).Should().BeNull();
    }

    [Theory]
    [InlineData(".JPEG", "image/jpeg")]
    [InlineData(".png", "image/png")]
    [InlineData(".webp", "image/webp")]
    [InlineData(".pdf", "application/pdf")]
    public void FindByExtension_KnownExtensions_AreCaseInsensitive(string extension, string expectedMime)
    {
        FileSignatureInspector.FindByExtension(extension)!.Mime.Should().Be(expectedMime);
    }

    [Fact]
    public void ResolveContentType_ContentMatchesExtension_UsesDetectedMime()
    {
        var contentType = FileSignatureInspector.ResolveContentType(
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A],
            "avatar.png",
            "application/octet-stream");

        contentType.Should().Be("image/png");
    }

    [Fact]
    public void ResolveContentType_ContentNotRecognised_KeepsDeclaredContentType()
    {
        var contentType = FileSignatureInspector.ResolveContentType(
            [0x01, 0x02, 0x03, 0x04],
            "firmware.bin",
            "application/octet-stream");

        contentType.Should().Be("application/octet-stream");
    }

    [Fact]
    public void ResolveContentType_NoDeclaredContentType_ReturnsEmptySoStorageInfersFromExtension()
    {
        FileSignatureInspector.ResolveContentType([0x01, 0x02, 0x03, 0x04], "firmware.bin", null)
            .Should().BeEmpty();
    }

    /// <summary>Dựng box <c>ftyp</c> của ISO Base Media với brand chỉ định ở byte 8..11.</summary>
    private static byte[] BuildIsoBaseMedia(string brand)
    {
        var header = new byte[16];
        header[3] = 0x18;
        "ftyp"u8.CopyTo(header.AsSpan(4));
        for (var i = 0; i < 4; i++)
            header[8 + i] = (byte)brand[i];

        return header;
    }
}
