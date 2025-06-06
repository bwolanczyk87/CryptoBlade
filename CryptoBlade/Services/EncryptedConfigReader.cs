using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CryptoBlade.Services;
public class DeepSeekAccountReader
{
    private readonly string _configFilePath;
    public DeepSeekAccountReader(string configFilePath)
    {
        _configFilePath = configFilePath;
    }
    public DeepSeekAccountConfig ReadConfig()
    {
        if (!File.Exists(_configFilePath))
            return new DeepSeekAccountConfig();

        var json = File.ReadAllText(_configFilePath);

        var root = JsonSerializer.Deserialize<DeepSeekRootConfig>(json);
        return root?.DeepSeek ?? new DeepSeekAccountConfig();
    }
}

public class DeepSeekRootConfig
{
    public DeepSeekAccountConfig? DeepSeek { get; set; }
}

public class DeepSeekAccountConfig
{
    public List<DeepSeekAccount>? Accounts { get; set; } = new();
}

public class DeepSeekAccount
{
    public string ApiName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}