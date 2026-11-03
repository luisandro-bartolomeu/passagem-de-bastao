using Core.Exceptions;
using FluentAssertions;
using Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace UnitTests;

public class LocalFileStorageServiceTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly LocalFileStorageService _sut;
    private readonly FileValidator _validator;

    private static readonly byte[] ValidPdfContent =
    [
        0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x35, // %PDF-1.5
        0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A              // Sample PDF body
    ];

    private static readonly byte[] ExecutableContent =
    [
        0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 // MZ DOS/PE header
    ];

    public LocalFileStorageServiceTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "academic_repo_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);

        var options = Options.Create(new LocalStorageOptions
        {
            StoragePath = _testDirectory,
            BaseUrl = "http://localhost:5000/uploads"
        });

        _validator = new FileValidator();
        _sut = new LocalFileStorageService(_validator, options);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
            catch
            {
                // Clean-up best effort in temp
            }
        }
    }

    [Fact]
    public async Task UploadAsync_WithValidPdfFile_ShouldSaveToDiskAndReturnResult()
    {
        using var stream = new MemoryStream(ValidPdfContent);

        var result = await _sut.UploadAsync(stream, "prova_calculo.pdf", "application/pdf");

        result.Should().NotBeNull();
        result.OriginalFileName.Should().Be("prova_calculo.pdf");
        result.ContentType.Should().Be("application/pdf");
        result.FileSizeBytes.Should().Be(ValidPdfContent.Length);
        result.StorageKey.Should().EndWith(".pdf");

        // Verify file physically exists on disk
        var fullPath = Path.Combine(_testDirectory, result.StorageKey);
        File.Exists(fullPath).Should().BeTrue();
    }

    [Fact]
    public async Task UploadAsync_WithExecutableRenamedToPdf_ShouldThrowInvalidFileFormatException()
    {
        using var stream = new MemoryStream(ExecutableContent);

        var act = async () => await _sut.UploadAsync(stream, "documento_falso.pdf", "application/pdf");

        await act.Should().ThrowAsync<InvalidFileFormatException>()
            .WithMessage("*executável*");

        // Verify no files were created
        Directory.GetFiles(_testDirectory, "*.*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task UploadAsync_WithOversizedFile_ShouldThrowFileSizeLimitExceededException()
    {
        var oversizedStream = new MockOversizedStream(50 * 1024 * 1024 + 1, ValidPdfContent);

        var act = async () => await _sut.UploadAsync(oversizedStream, "material_gigante.pdf", "application/pdf");

        await act.Should().ThrowAsync<FileSizeLimitExceededException>()
            .Where(e => e.MaxAllowedBytes == 50 * 1024 * 1024);

        // Verify no files were created
        Directory.GetFiles(_testDirectory, "*.*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task DownloadAsync_WithExistingFile_ShouldReturnStream()
    {
        using var uploadStream = new MemoryStream(ValidPdfContent);
        var uploadResult = await _sut.UploadAsync(uploadStream, "teste.pdf", "application/pdf");

        var downloadResult = await _sut.DownloadAsync(uploadResult.StorageKey);

        downloadResult.Should().NotBeNull();
        downloadResult!.ContentType.Should().Be("application/pdf");
        downloadResult.Content.Should().NotBeNull();

        using var memory = new MemoryStream();
        await downloadResult.Content.CopyToAsync(memory);
        memory.ToArray().Should().BeEquivalentTo(ValidPdfContent);

        downloadResult.Content.Dispose();
    }

    [Fact]
    public async Task DownloadAsync_WithNonExistentKey_ShouldReturnNull()
    {
        var result = await _sut.DownloadAsync("2026/01/non_existent.pdf");

        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_WithExistingFile_ShouldDeleteAndReturnTrue()
    {
        using var uploadStream = new MemoryStream(ValidPdfContent);
        var uploadResult = await _sut.UploadAsync(uploadStream, "to_delete.pdf", "application/pdf");

        var deleted = await _sut.DeleteAsync(uploadResult.StorageKey);
        deleted.Should().BeTrue();

        var fullPath = Path.Combine(_testDirectory, uploadResult.StorageKey);
        File.Exists(fullPath).Should().BeFalse();

        // Second delete should return false
        var secondDelete = await _sut.DeleteAsync(uploadResult.StorageKey);
        secondDelete.Should().BeFalse();
    }

    [Fact]
    public async Task GetFileUrlAsync_ShouldReturnFormattedUrl()
    {
        const string storageKey = "2026/11/abc12345.pdf";

        var url = await _sut.GetFileUrlAsync(storageKey);

        url.Should().Be("http://localhost:5000/uploads/2026/11/abc12345.pdf");
    }

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
