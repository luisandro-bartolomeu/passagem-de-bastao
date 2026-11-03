using Core.Enums;
using Core.Notifications;
using FluentAssertions;

namespace UnitTests;

public class MaterialSubmittedNotificationTests
{
    [Fact]
    public void ToEmailMessage_ShouldFormatSubjectAndBodyCorrectlyForExam()
    {
        var notification = new MaterialSubmittedNotification(
            MaterialId: Guid.NewGuid(),
            Title: "Exame de Cálculo Financeiro 2026",
            SubjectName: "Cálculo Financeiro",
            UploadedByUserName: "Manuel Delegado",
            UploadedByUserEmail: "manuel@isutic.ed.ao",
            MaterialType: MaterialType.Exam,
            CreatedAt: new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc)
        );

        const string adminEmail = "admin@isutic.ed.ao";
        var email = notification.ToEmailMessage(adminEmail);

        email.To.Should().Be(adminEmail);
        email.Subject.Should().Be("[IGF Académico] Novo Material Pendente: Exame de Cálculo Financeiro 2026");
        email.IsHtml.Should().BeTrue();
        email.Body.Should().Contain("Exame de Cálculo Financeiro 2026");
        email.Body.Should().Contain("Cálculo Financeiro");
        email.Body.Should().Contain("Prova / Enunciado");
        email.Body.Should().Contain("Manuel Delegado");
        email.Body.Should().Contain("manuel@isutic.ed.ao");
        email.Body.Should().Contain(notification.MaterialId.ToString());
    }

    [Fact]
    public void ToEmailMessage_ShouldFormatBodyCorrectlyForLectureNote()
    {
        var notification = new MaterialSubmittedNotification(
            MaterialId: Guid.NewGuid(),
            Title: "Sebenta Teórica de Contabilidade",
            SubjectName: "Contabilidade I",
            UploadedByUserName: "Ana Delegada",
            UploadedByUserEmail: "ana@isutic.ed.ao",
            MaterialType: MaterialType.LectureNote,
            CreatedAt: DateTime.UtcNow
        );

        var email = notification.ToEmailMessage("admin@isutic.ed.ao");

        email.Body.Should().Contain("Sebenta / Conteúdo");
        email.Body.Should().Contain("Sebenta Teórica de Contabilidade");
    }
}
