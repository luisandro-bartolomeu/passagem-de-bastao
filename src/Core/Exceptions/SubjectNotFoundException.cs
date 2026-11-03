namespace Core.Exceptions;

public class SubjectNotFoundException : Exception
{
    public int SubjectId { get; }

    public SubjectNotFoundException(int subjectId)
        : base($"A disciplina com identificador '{subjectId}' não foi encontrada.")
    {
        SubjectId = subjectId;
    }
}
