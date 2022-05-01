using GeneticSharp.Domain.Chromosomes;
using GeneticSharp.Domain.Randomizations;
using MMBot.Api.dto;
using MMBotGA.ga;

namespace strategy_plotter.Levels
{
    class LevelsStrategyChromosome : SpreadChromosome
    {
        public LevelsStrategyChromosome() : base(false)
        {
            LevelRange = Factory.Create(() => RandomizationProvider.Current.GetDouble(0.000001, 5));
            MaxLevelBudget = Factory.Create(() => RandomizationProvider.Current.GetDouble(0.000001, 1));
            InitialBetDistance = Factory.Create(() => RandomizationProvider.Current.GetDouble(0.000001, 1));
            MitigationLevel = Factory.Create(() => RandomizationProvider.Current.GetInt(0, 100));
            MitigationStrength = Factory.Create(() => RandomizationProvider.Current.GetDouble(0, 1));

            FinalizeGenes();
        }

        public GeneWrapper<double> LevelRange { get; }
        public GeneWrapper<double> MaxLevelBudget { get; }
        public GeneWrapper<double> InitialBetDistance { get; }
        public GeneWrapper<int> MitigationLevel { get; }
        public GeneWrapper<double> MitigationStrength { get; }

        public override IChromosome CreateNew() => new LevelsStrategyChromosome();

        public override Config ToConfig()
        {
            var res = base.ToConfig();
            res.Strategy = new LevelsStrategyConfig
            {
                Type = "levels",
                InitialBetDistance = InitialBetDistance,
                LevelRange = LevelRange,
                MaxLevelBudget = MaxLevelBudget,
                MitigationLevel = MitigationLevel,
                MitigationStrength = MitigationStrength
            };
            return res;
        }

        public override void FromConfig(Config config)
        {
            base.FromConfig(config);

            var s = config.ParseStrategyConfig<LevelsStrategyConfig>("levels");

            LevelRange.Replace(s.LevelRange);
            MaxLevelBudget.Replace(s.MaxLevelBudget);
            InitialBetDistance.Replace(s.InitialBetDistance);
            MitigationLevel.Replace(s.MitigationLevel);
            MitigationStrength.Replace(s.MitigationStrength);
        }
    }
}