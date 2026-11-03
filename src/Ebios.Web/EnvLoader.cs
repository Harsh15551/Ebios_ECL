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
            searchDirectory ??= Directory.GetCurrentDirectory();

            string? envFilePath = null;
            var dir = new DirectoryInfo(searchDirectory);

            // Walk upwards until we find .env or hit the root
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, ".env");
                if (File.Exists(candidate))
                {
                    envFilePath = candidate;
                    break;
                }
                dir = dir.Parent;
            }

            // Fallback: check base application directory
            if (envFilePath == null)
            {
                var baseCandidate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env");
                if (File.Exists(baseCandidate))
                {
                    envFilePath = baseCandidate;
                }
            }

            if (envFilePath == null || !File.Exists(envFilePath))
            {
                return;
            }

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

