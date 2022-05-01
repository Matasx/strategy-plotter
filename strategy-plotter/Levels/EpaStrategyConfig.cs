using System.Text.Json.Serialization;

namespace strategy_plotter.Levels
{
    public class LevelsStrategyConfig : IStrategyConfig
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("level_range")]
        public double LevelRange { get; set; }

        [JsonPropertyName("max_level_budget")]
        public double MaxLevelBudget { get; set; }

        [JsonPropertyName("initial_bet_distance")]
        public double InitialBetDistance { get; set; }

        [JsonPropertyName("mitigation_level")]
        public int MitigationLevel { get; set; }

        [JsonPropertyName("mitigation_strength")]
        public double MitigationStrength { get; set; }
    }
}