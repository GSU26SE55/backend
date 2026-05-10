namespace AuthService.Application.Interfaces.Helpers;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hashedPassword);
}
