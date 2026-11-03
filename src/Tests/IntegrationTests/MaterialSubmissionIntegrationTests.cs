using System.Net;
using System.Net.Http.Headers;
using Core.Entities;
using Core.Enums;
using Core.Interfaces;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IntegrationTests;

public class MaterialSubmissionIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly byte[] ValidPdfBytes =
    [
        0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x35, // %PDF-1.5
        0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A,             // Sample body
        0x25, 0x25, 0x45, 0x4F, 0x46                    // %%EOF
    ];

    private static readonly byte[] ExecutableBytes =
    [
        0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, // MZ executable header
        0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00
    ];

    public MaterialSubmissionIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private (WebApplicationFactory<Program> AppFactory, User Delegado, User Estudante, Subject Subject) CreateConfiguredEnvironment()
    {
        var inMemoryDbName = "IntegrationMaterials_" + Guid.NewGuid().ToString("N");

        var appFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                var internalServiceProvider = new ServiceCollection()
                    .AddEntityFrameworkInMemoryDatabase()
                    .BuildServiceProvider();

                services.AddDbContext<AppDbContext>(options =>
                {
                    options.UseInMemoryDatabase(inMemoryDbName);
                    options.UseInternalServiceProvider(internalServiceProvider);
                });
            });
        });

        using var scope = appFactory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var academicYear = new AcademicYear { Id = 1, YearLevel = 1, Name = "1.º Ano" };
        var subject = new Subject { Id = 1, YearLevel = 1, AcademicYear = academicYear, Code = "IGF101", Name = "Programação I" };
        var delegado = new User { Id = Guid.NewGuid(), Name = "Delegado Silva", Email = "delegado@isutic.ed.ao", Role = UserRole.Delegado, IsActive = true };
        var estudante = new User { Id = Guid.NewGuid(), Name = "Estudante Santos", Email = "estudante@isutic.ed.ao", Role = UserRole.Estudante, IsActive = true };

        db.AcademicYears.Add(academicYear);
        db.Subjects.Add(subject);
        db.Users.AddRange(delegado, estudante);
        db.SaveChanges();

        return (appFactory, delegado, estudante, subject);
    }

    private string GenerateToken(WebApplicationFactory<Program> factory, User user)
    {
        using var scope = factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        return jwtService.GenerateAccessToken(user);
    }

    [Fact]
    public async Task PostMaterial_WithoutAuth_ShouldReturn401Unauthorized()
    {
        var (factory, _, _, _) = CreateConfiguredEnvironment();
        var client = factory.CreateClient();

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("Título"), "title");

        var response = await client.PostAsync("/api/v1/materials", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostMaterial_AsStudent_ShouldReturn403Forbidden()
    {
        var (factory, _, estudante, _) = CreateConfiguredEnvironment();
        var client = factory.CreateClient();
        var studentToken = GenerateToken(factory, estudante);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", studentToken);

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("Título Proibido"), "title");

        var response = await client.PostAsync("/api/v1/materials", content);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostMaterial_AsDelegateWithValidData_ShouldReturn201CreatedAndPendingStatus()
    {
        var (factory, delegado, _, subject) = CreateConfiguredEnvironment();
        var client = factory.CreateClient();
        var delegateToken = GenerateToken(factory, delegado);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", delegateToken);

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("Exame Recurso 2026"), "title");
        content.Add(new StringContent("Enunciado oficial completo"), "description");
        content.Add(new StringContent("Exam"), "materialType");
        content.Add(new StringContent(subject.Id.ToString()), "subjectId");
        content.Add(new StringContent("Monitor João"), "peerHelperName");
        content.Add(new ByteArrayContent(ValidPdfBytes), "file", "exame_recurso.pdf");

        var response = await client.PostAsync("/api/v1/materials", content);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().StartWith("/api/v1/materials/");

        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("\"status\":\"Pending\"");
        json.Should().Contain("\"title\":\"Exame Recurso 2026\"");
        json.Should().Contain("\"subjectName\":\"Programação I\"");
        json.Should().Contain("\"uploadedByUserName\":\"Delegado Silva\"");
    }

    [Fact]
    public async Task PostMaterial_WithNonExistentSubject_ShouldReturn400BadRequest()
    {
        var (factory, delegado, _, _) = CreateConfiguredEnvironment();
        var client = factory.CreateClient();
        var delegateToken = GenerateToken(factory, delegado);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", delegateToken);

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("Prova"), "title");
        content.Add(new StringContent("Descrição"), "description");
        content.Add(new StringContent("Exam"), "materialType");
        content.Add(new StringContent("9999"), "subjectId"); // Subject does not exist
        content.Add(new ByteArrayContent(ValidPdfBytes), "file", "prova.pdf");

        var response = await client.PostAsync("/api/v1/materials", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("Disciplina Não Encontrada");
    }

    [Fact]
    public async Task PostMaterial_WithExecutableRenamedToPdf_ShouldReturn400BadRequest()
    {
        var (factory, delegado, _, subject) = CreateConfiguredEnvironment();
        var client = factory.CreateClient();
        var delegateToken = GenerateToken(factory, delegado);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", delegateToken);

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("Material Malicioso"), "title");
        content.Add(new StringContent("Descrição"), "description");
        content.Add(new StringContent("Exam"), "materialType");
        content.Add(new StringContent(subject.Id.ToString()), "subjectId");
        content.Add(new ByteArrayContent(ExecutableBytes), "file", "malicious.pdf");

        var response = await client.PostAsync("/api/v1/materials", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("Formato de Ficheiro Inválido");
    }

    [Fact]
    public async Task PostMaterial_WithOversizedFile_ShouldReturn413PayloadTooLarge()
    {
        var (factory, delegado, _, subject) = CreateConfiguredEnvironment();
        var client = factory.CreateClient();
        var delegateToken = GenerateToken(factory, delegado);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", delegateToken);

        // Create stream > 50 MB
        var oversizedStream = new MockOversizedStream(50 * 1024 * 1024 + 1, ValidPdfBytes);
        using var streamContent = new StreamContent(oversizedStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("Material Gigante"), "title");
        content.Add(new StringContent("Descrição"), "description");
        content.Add(new StringContent("Exam"), "materialType");
        content.Add(new StringContent(subject.Id.ToString()), "subjectId");
        content.Add(streamContent, "file", "huge.pdf");

        var response = await client.PostAsync("/api/v1/materials", content);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
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
            if (_position >= _length)
            {
                return 0;
            }

            var bytesToRead = (int)Math.Min(count, _length - _position);
            var headerBytesToRead = 0;

            if (_position < _header.Length)
            {
                headerBytesToRead = (int)Math.Min(bytesToRead, _header.Length - _position);
                Array.Copy(_header, _position, buffer, offset, headerBytesToRead);
            }

            var dummyBytesToRead = bytesToRead - headerBytesToRead;
            if (dummyBytesToRead > 0)
            {
                Array.Clear(buffer, offset + headerBytesToRead, dummyBytesToRead);
            }

            _position += bytesToRead;
            return bytesToRead;
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
