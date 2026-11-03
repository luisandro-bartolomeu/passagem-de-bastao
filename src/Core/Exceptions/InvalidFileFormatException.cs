namespace Core.Exceptions;

public class InvalidFileFormatException : Exception
{
    public InvalidFileFormatException(string message) : base(message)
    {
    }
}
