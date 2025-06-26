// =========================================================
// 1.  MODEL-SPECYFICZNA KONFIGURACJA
// =========================================================
namespace CryptoBlade.Strategies.AI
{
    /// <summary>
    /// Parametry charakterystyczne dla konkretnego modelu.
    /// </summary>
    public sealed class AIModelOptions
    {
        public required string ModelId { get; init; }   // np. deepseek-chat
        public required Uri Endpoint { get; init; }   // https://api.deepseek.com lub domyślny
        public required int MaxOutputTokens { get; init; }   // np. 500
        public required float Temperature { get; init; }   // np. 0.2f
        public required TimeSpan HttpTimeout { get; init; }   // np. 00:05:00
    }

    /// <summary>
    /// Centralne miejsce z domyślnymi presetami dla obsługiwanych modeli.
    /// Dodanie nowego modelu = dopisanie wpisu w słowniku.
    /// </summary>
    public static class AIModelCatalog
    {
        public static readonly IReadOnlyDictionary<string, AIModelOptions> Defaults =
            new Dictionary<string, AIModelOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["deepseek-chat"] = new AIModelOptions
                {
                    ModelId = "deepseek-chat",
                    Endpoint = new Uri("https://api.deepseek.com"),
                    MaxOutputTokens = 500,          // wyjście musi być krótkie
                    Temperature = 0.2f,
                    HttpTimeout = TimeSpan.FromSeconds(100) // domyślne w DeepSeek
                },
                ["deepseek-reasoner"] = new AIModelOptions
                {
                    ModelId = "deepseek-reasoner",
                    Endpoint = new Uri("https://api.deepseek.com"),
                    MaxOutputTokens = 1200,         // Reasoner generuje dłuższe CoT
                    Temperature = 0.2f,
                    HttpTimeout = TimeSpan.FromMinutes(5)
                },
                ["gpt-4o-mini"] = new AIModelOptions
                {
                    ModelId = "gpt-4o-mini",
                    Endpoint = new Uri("https://api.openai.com/v1"), // standard OpenAI
                    MaxOutputTokens = 750,
                    Temperature = 0.2f,
                    HttpTimeout = TimeSpan.FromSeconds(60)
                },
                ["gpt-4o"] = new AIModelOptions
                {
                    ModelId = "gpt-4o",
                    Endpoint = new Uri("https://api.openai.com/v1"),
                    MaxOutputTokens = 1000,
                    Temperature = 0.2f,
                    HttpTimeout = TimeSpan.FromSeconds(90)
                }
            };
    }
}
