using HomeBudget.Api.Features.Attachments;

namespace HomeBudget.Tests.Domain;

public sealed class FileSignatureTests
{
    public static TheoryData<byte[], string?> Samples => new()
    {
        { [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10], FileSignature.Jpeg },
        { [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0], FileSignature.Png },
        { "RIFF\0\0\0\0WEBPVP8 "u8.ToArray(), FileSignature.WebP },
        { "%PDF-1.7\n"u8.ToArray(), FileSignature.Pdf },
        { "\0\0\0\u0018ftypheic\0\0\0\0"u8.ToArray(), FileSignature.Heic },
        { "\0\0\0\u0018ftypmif1\0\0\0\0"u8.ToArray(), FileSignature.Heic },
        // Vidéo MP4 : conteneur ISO-BMFF, mais pas une image HEIF.
        { "\0\0\0\u0018ftypisom\0\0\0\0"u8.ToArray(), null },
        { "RIFF\0\0\0\0WAVEfmt "u8.ToArray(), null },
        { "<html><script>"u8.ToArray(), null },
        { "<svg xmlns="u8.ToArray(), null },
        { "MZ\u0090\0"u8.ToArray(), null },
        { "PK\u0003\u0004"u8.ToArray(), null },
        { [], null },
    };

    [Theory]
    [MemberData(nameof(Samples))]
    public void Type_is_detected_from_the_first_bytes(byte[] header, string? expected) =>
        FileSignature.Detect(header).ShouldBe(expected);

    [Fact]
    public void Only_images_are_displayed_inline()
    {
        FileSignature.IsInline(FileSignature.Jpeg).ShouldBeTrue();
        FileSignature.IsInline(FileSignature.WebP).ShouldBeTrue();
        FileSignature.IsInline(FileSignature.Pdf).ShouldBeFalse();
        FileSignature.IsInline(FileSignature.Heic).ShouldBeFalse();
    }

    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("C:\\temp\\fiche.pdf", "fiche.pdf")]
    [InlineData("  ", "piece-jointe.pdf")]
    [InlineData("ticket\u0000\u001f.jpg", "ticket.jpg")]
    public void File_name_is_cleaned(string name, string expected) =>
        AttachmentStore.CleanFileName(name, FileSignature.Pdf).ShouldBe(expected);
}
