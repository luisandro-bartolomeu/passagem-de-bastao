using Core.Enums;

namespace Core.Entities;

public class Material
{
    public Guid Id { get; set; }
    public int SubjectId { get; set; }
    public Subject Subject { get; set; } = null!;
    public Guid UploadedByUserId { get; set; }
    public User UploadedByUser { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public MaterialType MaterialType { get; set; }
    public string FileStorageKey { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public MaterialStatus Status { get; set; } = MaterialStatus.Pending;
    public string? RejectionReason { get; set; }
    public string? PeerHelperName { get; set; }
    public string? PeerHelperContact { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
