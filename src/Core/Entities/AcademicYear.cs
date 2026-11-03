namespace Core.Entities;

public class AcademicYear
{
    public int Id { get; set; }
    public int YearLevel { get; set; }
    public string Name { get; set; } = string.Empty;

    public ICollection<Subject> Subjects { get; set; } = new List<Subject>();
}
