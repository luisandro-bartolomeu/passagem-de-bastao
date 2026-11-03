namespace Core.Entities;

public class Subject
{
    public int Id { get; set; }
    public int YearLevel { get; set; }
    public AcademicYear AcademicYear { get; set; } = null!;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Material> Materials { get; set; } = new List<Material>();
}
