namespace Ebios.Web;

public static class EnvLoader
{
    /// <summary>
    /// Searches for .env file starting from the app's current directory up to parent roots,
    /// parses key-value pairs, and sets them into System.Environment.
    /// </summary>
    public static void Load(string? searchDirectory = null)
    {
        try
        {
            string? envFilePath = null;

            // 1. First priority: Check the application's base directory (where Ebios.Web.dll is deployed)
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            if (!string.IsNullOrEmpty(baseDir))
            {
                var baseCandidate = Path.Combine(baseDir, ".env");
                var baseTxtCandidate = Path.Combine(baseDir, ".env.txt");
                if (File.Exists(baseCandidate))
                {
                    envFilePath = baseCandidate;
                }
                else if (File.Exists(baseTxtCandidate))
                {
                    envFilePath = baseTxtCandidate;
                }
            }

            // 2. Second priority: Walk upwards from current working directory
            if (envFilePath == null)
            {
                searchDirectory ??= Directory.GetCurrentDirectory();
                var dir = new DirectoryInfo(searchDirectory);

                while (dir != null)
                {
                    var candidate = Path.Combine(dir.FullName, ".env");
                    var candidateTxt = Path.Combine(dir.FullName, ".env.txt");
                    if (File.Exists(candidate))
                    {
                        envFilePath = candidate;
                        break;
                    }
                    if (File.Exists(candidateTxt))
                    {
                        envFilePath = candidateTxt;
                        break;
                    }
                    dir = dir.Parent;
                }
            }

            if (envFilePath == null || !File.Exists(envFilePath))
            {
                Console.WriteLine("[EnvLoader] Notice: No .env file found; falling back to appsettings.json and system environment variables.");
                return;
            }

            Console.WriteLine($"[EnvLoader] Loaded environment from: {envFilePath}");
            var lines = File.ReadAllLines(envFilePath);
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                {
                    continue;
                }

                int equalIndex = line.IndexOf('=');
                if (equalIndex <= 0) continue;

                string key = line.Substring(0, equalIndex).Trim();
                string val = line.Substring(equalIndex + 1).Trim().Trim('"', '\'');

                Environment.SetEnvironmentVariable(key, val);

                // Map standard shorthand variables to ASP.NET Core hierarchical configuration keys
                switch (key.ToUpperInvariant())
                {
                    case "DB_CONNECTION_STRING":
                        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", val);
                        break;
                    case "EBIOS_URL":
                        Environment.SetEnvironmentVariable("EbiosServer__Url", val);
                        break;
                    case "EBIOS_USERNAME":
                        Environment.SetEnvironmentVariable("EbiosServer__UserName", val);
                        break;
                    case "EBIOS_PASSWORD":
                        Environment.SetEnvironmentVariable("EbiosServer__Password", val);
                        break;
                    case "EBIOS_WEBHOOK_KEY":
                        Environment.SetEnvironmentVariable("EbiosServer__WebhookKey", val);
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EnvLoader] Warning: Unable to parse .env file: {ex.Message}");
        }
    }
}

