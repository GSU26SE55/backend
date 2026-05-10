namespace SharedInfrastructure.Extensions;

/// <summary>
/// Load `.env` file cho local development. Trong Docker compose, env vars được inject sẵn
/// qua `env_file:` directive (trỏ tới `.env.Docker`) → file `.env` không tồn tại trong container,
/// helper này bỏ qua silently.
///
/// Pattern:
/// - Local: dotnet run → tìm `.env` ở repo root → set env vars cho process.
/// - Docker: compose inject sẵn từ `.env.Docker` → helper không tìm thấy `.env` → no-op.
///
/// Helper này KHÔNG ghi đè env var đã được set từ trước (giữ priority cho values inject từ outside).
///
/// Cách dùng: gọi `EnvFileLoader.LoadIfExists()` ngay đầu Program.cs trước WebApplication.CreateBuilder.
/// </summary>
public static class EnvFileLoader
{
    public static void LoadIfExists()
    {
        var envPath = FindEnvFile(AppContext.BaseDirectory) ?? FindEnvFile(Directory.GetCurrentDirectory());

        if (envPath is null)
        {
            Console.WriteLine("[EnvFileLoader] No .env file found. Using ambient environment variables.");
            return;
        }

        var loaded = 0;
        foreach (var line in File.ReadAllLines(envPath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                continue;

            var sepIndex = trimmed.IndexOf('=');
            if (sepIndex <= 0)
                continue;

            var key = trimmed[..sepIndex].Trim();
            var value = trimmed[(sepIndex + 1)..].Trim();

            // Strip surrounding quotes if present
            if (value.Length >= 2 && ((value.StartsWith('"') && value.EndsWith('"')) ||
                                       (value.StartsWith('\'') && value.EndsWith('\''))))
            {
                value = value[1..^1];
            }

            // KHÔNG ghi đè env var đã set (ambient từ Docker compose có priority)
            if (Environment.GetEnvironmentVariable(key) is null)
            {
                Environment.SetEnvironmentVariable(key, value);
                loaded++;
            }
        }

        Console.WriteLine($"[EnvFileLoader] Loaded {loaded} env vars from: {envPath}");
    }

    private static string? FindEnvFile(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            var envPath = Path.Combine(directory.FullName, ".env");
            if (File.Exists(envPath))
                return envPath;

            directory = directory.Parent;
        }
        return null;
    }
}
