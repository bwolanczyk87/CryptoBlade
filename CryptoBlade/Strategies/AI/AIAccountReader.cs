using System.Text.Json;

namespace CryptoBlade.Strategies.AI;
public class AIAccountReader(string configFilePath)
{
    private readonly string _configFilePath = configFilePath;

    public AiAccountsRoot ReadConfig()
    {
        if (!File.Exists(_configFilePath))
            throw new FileNotFoundException($"AI accounts configuration file not found: {_configFilePath}");

        var json = File.ReadAllText(_configFilePath);
        return JsonSerializer.Deserialize<AiAccountsRoot>(json);
    }
}

public class AiAccountsRoot
{
    public List<AiAccount> AiAccounts { get; set; } = [];
}

public class AiAccount
{
    public string ApiName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public int MaxOutputTokens { get; set; }
    public float Temperature { get; set; }
    public float FrequencyPenalty { get; set; }
    public int HttpTimeout { get; set; }
}