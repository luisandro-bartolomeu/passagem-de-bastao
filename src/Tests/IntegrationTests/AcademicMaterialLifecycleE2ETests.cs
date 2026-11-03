using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Core.DTOs;
using Core.Entities;
using Core.Enums;
using Core.Interfaces;
using FluentAssertions;
using Infrastructure.Data;
using Infrastructure.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IntegrationTests;

public class AcademicMaterialLifecycleE2ETests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly byte[] ValidPdfBytes =
    [
        0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x35, // %PDF-1.5
        0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A,             // binary body sample
        0x25, 0x25, 0x45, 0x4F, 0x46                    // %%EOF
    ];

    public AcademicMaterialLifecycleE2ETests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private class TestEmailSender : IEmailSender
    {
        public List<EmailMessage> SentMessages { get; } = new();

        public Task SendEmailAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            lock (SentMessages)
            {
                SentMessages.Add(message);
            }
            return Task.CompletedTask;
        }
    }

    private class E2ETestEnvironment
    {
        public required WebApplicationFactory<Program> Factory { get; init; }
        public required User Admin { get; init; }
        public required User Delegado { get; init; }
        public required User Estudante { get; init; }
        public required Subject Subject { get; init; }
        public required AcademicYear AcademicYear { get; init; }
        public required TestEmailSender EmailSender { get; init; }
        public string RawDelegadoPassword { get; } = "DelegadoSecret123!";
        public string RawAdminPassword { get; } = "AdminSecret123!";
        public string RawEstudantePassword { get; } = "EstudanteSecret123!";
    }

    private E2ETestEnvironment CreateEnvironment()
    {
        var dbName = "E2E_Db_" + Guid.NewGuid().ToString("N");
        var testEmailSender = new TestEmailSender();

        var appFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(WebHostDefaults.EnvironmentKey, "Testing");

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
                    options.UseInMemoryDatabase(dbName);
                    options.UseInternalServiceProvider(internalServiceProvider);
                });

                // Replace IEmailSender with captured TestEmailSender
                var emailSenderDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IEmailSender));
                if (emailSenderDescriptor != null)
                {
                    services.Remove(emailSenderDescriptor);
                }
                services.AddSingleton<IEmailSender>(testEmailSender);
            });
        });

        using var scope = appFactory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var year = new AcademicYear { Id = 1, YearLevel = 1, Name = "1.º Ano Curricular" };
        var subject = new Subject { Id = 1, YearLevel = 1, AcademicYear = year, Code = "IGF101", Name = "Programação I" };

        var delegado = new User
        {
            Id = Guid.NewGuid(),
            Name = "Delegado Silva",
            Email = "delegado.e2e@isutic.ed.ao",
            PasswordHash = hasher.HashPassword("DelegadoSecret123!"),
            Role = UserRole.Delegado,
            IsActive = true
        };

        var admin = new User
        {
            Id = Guid.NewGuid(),
            Name = "Administrador Geral",
            Email = "admin.e2e@isutic.ed.ao",
            PasswordHash = hasher.HashPassword("AdminSecret123!"),
            Role = UserRole.Admin,
            IsActive = true
        };

        var estudante = new User
        {
            Id = Guid.NewGuid(),
            Name = "Estudante Santos",
            Email = "estudante.e2e@isutic.ed.ao",
            PasswordHash = hasher.HashPassword("EstudanteSecret123!"),
            Role = UserRole.Estudante,
            IsActive = true
        };

        db.AcademicYears.Add(year);
        db.Subjects.Add(subject);
        db.Users.AddRange(admin, delegado, estudante);
        db.SaveChanges();

        return new E2ETestEnvironment
        {
            Factory = appFactory,
            Admin = admin,
            Delegado = delegado,
            Estudante = estudante,
            Subject = subject,
            AcademicYear = year,
            EmailSender = testEmailSender
        };
    }

    [Fact]
    public async Task FullAcademicMaterialLifecycle_EndToEnd_ShouldExecuteSuccessfully()
    {
        var env = CreateEnvironment();
        var client = env.Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost")
        });

        // -----------------------------------------------------------------------------------------
        // Step A: Login & Session Acquisition for Delegado, Admin, and Estudante
        // -----------------------------------------------------------------------------------------
        var delegadoLoginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(
            Email: env.Delegado.Email,
            Password: env.RawDelegadoPassword
        ));
        delegadoLoginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var delegadoAuth = await delegadoLoginResponse.Content.ReadFromJsonAsync<AuthResponseDto>();
        delegadoAuth.Should().NotBeNull();
        delegadoAuth!.AccessToken.Should().NotBeNullOrWhiteSpace();
        delegadoAuth.User.Role.Should().Be("Delegado");

        // Verify Set-Cookie header is issued for Refresh Token
        delegadoLoginResponse.Headers.TryGetValues("Set-Cookie", out var setCookies).Should().BeTrue();
        setCookies.Should().Contain(c => c.Contains(AuthCookieExtensions.RefreshTokenCookieName) && c.Contains("httponly"));

        // Login as Admin
        var adminLoginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(
            Email: env.Admin.Email,
            Password: env.RawAdminPassword
        ));
        adminLoginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var adminAuth = await adminLoginResponse.Content.ReadFromJsonAsync<AuthResponseDto>();
        adminAuth.Should().NotBeNull();
        var adminToken = adminAuth!.AccessToken;

        // Login as Estudante
        var estudanteLoginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(
            Email: env.Estudante.Email,
            Password: env.RawEstudantePassword
        ));
        estudanteLoginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var estudanteAuth = await estudanteLoginResponse.Content.ReadFromJsonAsync<AuthResponseDto>();
        estudanteAuth.Should().NotBeNull();
        var estudanteToken = estudanteAuth!.AccessToken;

        // -----------------------------------------------------------------------------------------
        // Step B: Delegado Submits Multipart Material (Initial Status Must Be "Pending")
        // -----------------------------------------------------------------------------------------
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", delegadoAuth.AccessToken);

        using var fileContent = new ByteArrayContent(ValidPdfBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");

        using var multipartForm = new MultipartFormDataContent();
        multipartForm.Add(new StringContent("Exame Oficial de Programação I - 2025"), "title");
        multipartForm.Add(new StringContent("Enunciado oficial completo com critérios de correção."), "description");
        multipartForm.Add(new StringContent("Exam"), "materialType");
        multipartForm.Add(new StringContent(env.Subject.Id.ToString()), "subjectId");
        multipartForm.Add(new StringContent("Monitor João Carlos"), "peerHelperName");
        multipartForm.Add(new StringContent("joao.carlos@isutic.ed.ao"), "peerHelperContact");
        multipartForm.Add(fileContent, "file", "exame_prog1_2025.pdf");

        var submitResponse = await client.PostAsync("/api/v1/materials", multipartForm);

        submitResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        submitResponse.Headers.Location.Should().NotBeNull();

        var createdMaterial = await submitResponse.Content.ReadFromJsonAsync<MaterialResponseDto>();
        createdMaterial.Should().NotBeNull();
        createdMaterial!.Status.Should().Be("Pending");
        createdMaterial.Title.Should().Be("Exame Oficial de Programação I - 2025");
        createdMaterial.SubjectName.Should().Be(env.Subject.Name);
        createdMaterial.UploadedByUserName.Should().Be(env.Delegado.Name);

        var materialId = createdMaterial.Id;

        // Verify notification is delivered by the background service
        var timeout = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < timeout && env.EmailSender.SentMessages.Count == 0)
        {
            await Task.Delay(50);
        }

        lock (env.EmailSender.SentMessages)
        {
            env.EmailSender.SentMessages.Should().NotBeEmpty();
            env.EmailSender.SentMessages[0].Subject.Should().Contain("Exame Oficial de Programação I - 2025");
            env.EmailSender.SentMessages[0].Body.Should().Contain(env.Delegado.Name);
        }

        // -----------------------------------------------------------------------------------------
        // Step C: Verify Catalog Isolation - Pending Material MUST NOT Appear in Public Listing
        // -----------------------------------------------------------------------------------------
        client.DefaultRequestHeaders.Authorization = null; // Anonymous / public access
        var publicCatalogResponse = await client.GetAsync("/api/v1/materials");
        publicCatalogResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var catalogBeforeApproval = await publicCatalogResponse.Content.ReadFromJsonAsync<PagedResult<MaterialResponseDto>>();
        catalogBeforeApproval.Should().NotBeNull();
        catalogBeforeApproval!.TotalCount.Should().Be(0);
        catalogBeforeApproval.Items.Should().BeEmpty();

        // Download must also return 404 while pending
        var prematureDownload = await client.GetAsync($"/api/v1/materials/{materialId}/download");
        prematureDownload.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // -----------------------------------------------------------------------------------------
        // Step D: Admin Approves Material (Status Transition to "Published")
        // -----------------------------------------------------------------------------------------
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var approvalRequest = new UpdateMaterialStatusRequest(
            Status: "Published",
            RejectionReason: null
        );

        var approvalResponse = await client.PatchAsJsonAsync($"/api/v1/admin/materials/{materialId}/status", approvalRequest);
        approvalResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var approvedMaterial = await approvalResponse.Content.ReadFromJsonAsync<MaterialResponseDto>();
        approvedMaterial.Should().NotBeNull();
        approvedMaterial!.Status.Should().Be("Published");
        approvedMaterial.RejectionReason.Should().BeNull();
        approvedMaterial.UpdatedAt.Should().NotBeNull();

        // -----------------------------------------------------------------------------------------
        // Step E: Estudante Queries Public Catalog & Locates Published Material
        // -----------------------------------------------------------------------------------------
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", estudanteToken);

        var studentCatalogResponse = await client.GetAsync($"/api/v1/materials?yearLevel=1&subjectId={env.Subject.Id}&materialType=Exam");
        studentCatalogResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var pagedCatalog = await studentCatalogResponse.Content.ReadFromJsonAsync<PagedResult<MaterialResponseDto>>();
        pagedCatalog.Should().NotBeNull();
        pagedCatalog!.TotalCount.Should().Be(1);
        pagedCatalog.Items.Should().HaveCount(1);
        pagedCatalog.Items[0].Id.Should().Be(materialId);
        pagedCatalog.Items[0].Status.Should().Be("Published");

        // -----------------------------------------------------------------------------------------
        // Step F: Estudante Downloads Binary File via Streaming
        // -----------------------------------------------------------------------------------------
        var downloadResponse = await client.GetAsync($"/api/v1/materials/{materialId}/download");
        downloadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        downloadResponse.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        downloadResponse.Content.Headers.ContentDisposition.Should().NotBeNull();
        downloadResponse.Content.Headers.ContentDisposition!.FileName.Should().Contain("exame_prog1_2025.pdf");

        var binaryBytes = await downloadResponse.Content.ReadAsByteArrayAsync();
        binaryBytes.Should().Equal(ValidPdfBytes);

        // -----------------------------------------------------------------------------------------
        // Step G: Delegado Attempts to Edit or Delete Published Material -> Must Receive HTTP 403
        // -----------------------------------------------------------------------------------------
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", delegadoAuth.AccessToken);

        // Attempt PUT on Published material
        var putRequest = new UpdateMaterialRequest(
            Title: "Tentativa de Alteração não Permitida",
            Description: "Tentando modificar material já aprovado",
            MaterialType: "Exam",
            SubjectId: env.Subject.Id,
            PeerHelperName: null,
            PeerHelperContact: null
        );

        var blockedPutResponse = await client.PutAsJsonAsync($"/api/v1/materials/{materialId}", putRequest);
        blockedPutResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var putError = await blockedPutResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        putError.Should().NotBeNull();
        putError!.Detail.Should().Contain("não podem ser alterados por delegados");

        // Attempt DELETE on Published material
        var blockedDeleteResponse = await client.DeleteAsync($"/api/v1/materials/{materialId}");
        blockedDeleteResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var deleteError = await blockedDeleteResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        deleteError.Should().NotBeNull();
        deleteError!.Detail.Should().Contain("não podem ser eliminados por delegados");

        // -----------------------------------------------------------------------------------------
        // Step H: Admin Removes Material (Physical File Cleanup and Database Deletion)
        // -----------------------------------------------------------------------------------------
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var adminDeleteResponse = await client.DeleteAsync($"/api/v1/materials/{materialId}");
        adminDeleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify material is now completely gone (404)
        var verifyGet = await client.GetAsync($"/api/v1/materials/{materialId}");
        verifyGet.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var verifyDownload = await client.GetAsync($"/api/v1/materials/{materialId}/download");
        verifyDownload.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SessionRotationFlow_UsingRefreshToken_ShouldRenewSessionSuccessfully()
    {
        var env = CreateEnvironment();
        var client = env.Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost")
        });

        // 1. Initial login
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(
            Email: env.Delegado.Email,
            Password: env.RawDelegadoPassword
        ));
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginAuth = await loginResponse.Content.ReadFromJsonAsync<AuthResponseDto>();
        loginAuth.Should().NotBeNull();

        // 2. Session rotation via POST /api/v1/auth/refresh using automatic cookie forwarding
        var refreshResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshTokenRequest());
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var refreshBody = await refreshResponse.Content.ReadFromJsonAsync<JsonElement>();
        var newAccessToken = refreshBody.GetProperty("accessToken").GetString();
        newAccessToken.Should().NotBeNullOrWhiteSpace();
        newAccessToken.Should().NotBe(loginAuth!.AccessToken);

        // 3. Verify the new access token can be used to query the API
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newAccessToken);
        var response = await client.GetAsync("/api/v1/materials");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MaterialRejectionFlow_WhenAdminRejects_ShouldNotExposeMaterialToPublicCatalog()
    {
        var env = CreateEnvironment();
        var client = env.Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost")
        });

        // 1. Delegado Login & Submit
        var delegadoLogin = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(env.Delegado.Email, env.RawDelegadoPassword));
        var delegadoAuth = await delegadoLogin.Content.ReadFromJsonAsync<AuthResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", delegadoAuth!.AccessToken);

        using var fileContent = new ByteArrayContent(ValidPdfBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("Sebenta Incompleta"), "title");
        form.Add(new StringContent("Conteúdo com páginas ilegíveis"), "description");
        form.Add(new StringContent("LectureNote"), "materialType");
        form.Add(new StringContent(env.Subject.Id.ToString()), "subjectId");
        form.Add(fileContent, "file", "sebenta.pdf");

        var submitRes = await client.PostAsync("/api/v1/materials", form);
        submitRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await submitRes.Content.ReadFromJsonAsync<MaterialResponseDto>();

        // 2. Admin Rejection with Reason
        var adminLogin = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(env.Admin.Email, env.RawAdminPassword));
        var adminAuth = await adminLogin.Content.ReadFromJsonAsync<AuthResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminAuth!.AccessToken);

        var rejectionReason = "Resolução gráfica insuficiente; páginas 3 e 4 cortadas.";
        var rejectRes = await client.PatchAsJsonAsync($"/api/v1/admin/materials/{created!.Id}/status", new UpdateMaterialStatusRequest(
            Status: "Rejected",
            RejectionReason: rejectionReason
        ));
        rejectRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var rejectedMaterial = await rejectRes.Content.ReadFromJsonAsync<MaterialResponseDto>();
        rejectedMaterial!.Status.Should().Be("Rejected");
        rejectedMaterial.RejectionReason.Should().Be(rejectionReason);

        // 3. Public catalog must not list rejected material
        client.DefaultRequestHeaders.Authorization = null;
        var catalogRes = await client.GetAsync("/api/v1/materials");
        var catalog = await catalogRes.Content.ReadFromJsonAsync<PagedResult<MaterialResponseDto>>();
        catalog!.TotalCount.Should().Be(0);

        // 4. Download must return 404
        var downloadRes = await client.GetAsync($"/api/v1/materials/{created.Id}/download");
        downloadRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
