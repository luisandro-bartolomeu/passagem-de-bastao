namespace Core.Exceptions;

public class MaterialNotFoundException : Exception
{
    public Guid MaterialId { get; }

    public MaterialNotFoundException(Guid materialId)
        : base($"Material com identificador '{materialId}' não foi encontrado.")
    {
        MaterialId = materialId;
    }
}
