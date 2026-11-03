using Core.Enums;
using Core.Interfaces;

namespace Core.Notifications;

public record MaterialSubmittedNotification(
    Guid MaterialId,
    string Title,
    string SubjectName,
    string UploadedByUserName,
    string UploadedByUserEmail,
    MaterialType MaterialType,
    DateTime CreatedAt
)
{
    public EmailMessage ToEmailMessage(string adminEmail)
    {
        var subject = $"[IGF Académico] Novo Material Pendente: {Title}";
        var typeLabel = MaterialType == MaterialType.Exam ? "Prova / Enunciado" : "Sebenta / Conteúdo";
        var body = $"""
            <h2>Novo Material Académico Submetido</h2>
            <p>Um delegado de turma submeteu um novo material que aguarda a sua moderação:</p>
            <ul>
                <li><strong>Título:</strong> {Title}</li>
                <li><strong>Disciplina:</strong> {SubjectName}</li>
                <li><strong>Tipo:</strong> {typeLabel}</li>
                <li><strong>Submetido por:</strong> {UploadedByUserName} ({UploadedByUserEmail})</li>
                <li><strong>Data/Hora (UTC):</strong> {CreatedAt:dd/MM/yyyy HH:mm:ss}</li>
                <li><strong>Identificador (ID):</strong> {MaterialId}</li>
            </ul>
            <p>Por favor aceda ao painel administrativo para aprovar ou rejeitar este material.</p>
            """;

        return new EmailMessage(adminEmail, subject, body, IsHtml: true);
    }
}
