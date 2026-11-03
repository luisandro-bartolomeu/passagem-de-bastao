using FluentAssertions;
using Infrastructure.Storage;

namespace UnitTests;

public class FileValidatorTests
{
    private readonly FileValidator _validator = new();

    // Valid header byte helpers
    private static readonly byte[] ValidPdfHeader = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x35]; // %PDF-1.5
    private static readonly byte[] ValidPngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] ValidJpegHeader = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46]; // JFIF
    private static readonly byte[] ValidPptHeader = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]; // OLE2
    private static readonly byte[] ValidPptxHeader = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x06, 0x00]; // Zip PK

    // Dangerous/Executable headers
    private static readonly byte[] WindowsExeHeader = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00]; // MZ
    private static readonly byte[] LinuxElfHeader = [0x7F, 0x45, 0x4C, 0x46, 0x02, 0x01, 0x01, 0x00]; // .ELF

    [Theory]
    [InlineData(".pdf", "application/pdf")]
    [InlineData(".png", "image/png")]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".jpeg", "image/jpeg")]
    [InlineData(".ppt", "application/vnd.ms-powerpoint")]
    [InlineData(".pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation")]
    public void Validate_WithValidMagicBytesAndExtension_ShouldReturnSuccess(string ext, string expectedContentType)
    {
        var header = ext switch
        {
            ".pdf" => ValidPdfHeader,
            ".png" => ValidPngHeader,
            ".jpg" or ".jpeg" => ValidJpegHeader,
            ".ppt" => ValidPptHeader,
            ".pptx" => ValidPptxHeader,
            _ => throw new ArgumentOutOfRangeException(nameof(ext))
        };

        using var stream = new MemoryStream(header);
        var result = _validator.Validate(stream, $"document{ext}");

        result.IsValid.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        result.DetectedContentType.Should().Be(expectedContentType);
        result.NormalizedExtension.Should().Be(ext);
    }

    [Theory]
    [InlineData(".pdf")]
    [InlineData(".png")]
    [InlineData(".jpg")]
    [InlineData(".ppt")]
    [InlineData(".pptx")]
    public void Validate_WithExecutableRenamedToAllowedExtension_ShouldReject(string ext)
    {
        // Windows Executable (MZ) renamed to .pdf, .png, etc.
        using var stream = new MemoryStream(WindowsExeHeader);
        var result = _validator.Validate(stream, $"malicious{ext}");

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("executável");
    }

    [Fact]
    public void Validate_WithElfExecutableRenamedToPdf_ShouldReject()
    {
        using var stream = new MemoryStream(LinuxElfHeader);
        var result = _validator.Validate(stream, "script.pdf");

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("executável");
    }

    [Fact]
    public void Validate_WithTextFileRenamedToPdf_ShouldReject()
    {
        var plainTextBytes = System.Text.Encoding.UTF8.GetBytes("This is just plain text content without PDF header");
        using var stream = new MemoryStream(plainTextBytes);

        var result = _validator.Validate(stream, "notes.pdf");

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("magic bytes");
    }

    [Fact]
    public void Validate_WithPngRenamedToPptx_ShouldRejectDueToMismatch()
    {
        using var stream = new MemoryStream(ValidPngHeader);

        var result = _validator.Validate(stream, "slides.pptx");

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("magic bytes");
    }

    [Theory]
    [InlineData("virus.exe")]
    [InlineData("script.sh")]
    [InlineData("archive.zip")]
    [InlineData("document.docx")]
    [InlineData("video.mp4")]
    [InlineData("unnamed")]
    public void Validate_WithDisallowedExtension_ShouldReject(string fileName)
    {
        using var stream = new MemoryStream(ValidPdfHeader);

        var result = _validator.Validate(stream, fileName);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("não permitida");
    }

    [Fact]
    public void Validate_WithFileExceeding50MB_ShouldReject()
    {
        // Custom stream that mocks length > 50 MB without allocating 51 MB in memory
        var oversizedStream = new MockOversizedStream(50 * 1024 * 1024 + 1, ValidPdfHeader);

        var result = _validator.Validate(oversizedStream, "huge.pdf");

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("50 MB");
    }

    [Fact]
    public void Validate_ShouldPreserveStreamPosition()
    {
        using var stream = new MemoryStream();
        stream.Write(ValidPdfHeader);
        stream.Write([0x01, 0x02, 0x03]);
        stream.Position = 0;

        _validator.Validate(stream, "test.pdf");

        stream.Position.Should().Be(0);
    }

    // Helper mock stream for testing oversized file without memory allocation
    private class MockOversizedStream : Stream
    {
        private readonly long _length;
        private readonly byte[] _header;
        private long _position;

        public MockOversizedStream(long length, byte[] header)
        {
            _length = length;
            _header = header;
            _position = 0;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position
        {
            get => _position;
            set => _position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _header.Length)
            {
                return 0;
            }

            var toCopy = (int)Math.Min(count, _header.Length - _position);
            Array.Copy(_header, _position, buffer, offset, toCopy);
            _position += toCopy;
            return toCopy;
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            _position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _length + offset,
                _ => _position
            };

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
