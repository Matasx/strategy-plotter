namespace strategy_plotter.Levels
{
    class LevelsStrategy : IStrategyPrototype<LevelsStrategyChromosome>
    {
        // State
        private record Level(double Anchor, Range Range, double Ep, double Enter, double Asset);
        private record Range(double Min, double Max);
        private readonly List<Level> _levels = new();

        // Settings
        double _levelRange = 0.1; // Level depth in % of price change
        double _maxLevelBudget = 0.1;
        //double _levelRangeOverlap = 0.01; // Overlap of multiple levels
        double _initialBetDistance = 0.5;
        int _mitigationLevel = 3; // From which level to start mitigate position
        double _mitigationStrength = 1; // 0-100% of normalized profit to use to cover for position on higher levels

        public IStrategyPrototype<LevelsStrategyChromosome> CreateInstance(LevelsStrategyChromosome chromosome)
        {
            return new LevelsStrategy
            {
                _levelRange = chromosome.LevelRange,
                _maxLevelBudget = chromosome.MaxLevelBudget,
                _initialBetDistance = chromosome.InitialBetDistance,
                _mitigationLevel = chromosome.MitigationLevel,
                _mitigationStrength = chromosome.MitigationStrength
            };
        }

        public double Evaluate(IEnumerable<Trade> trades, double budget, long timeFrame)
        {
            var t = trades.ToList();
            if (!t.Any()) return 0;

            // continuity -> stable performance and delivery of budget extra
            // get profit at least every 14 days
            var frames = (int)(TimeSpan.FromMilliseconds(timeFrame).TotalDays / 25);
            var gk = timeFrame / frames;
            var lastBudgetExtra = 0d;
            var minFitness = double.MaxValue;

            for (var i = 0; i < frames; i++)
            {
                var f0 = gk * i;
                var f1 = gk * (i + 1);
                var frameTrades = t
                    .SkipWhile(x => x.Time < f0)
                    .TakeWhile(x => x.Time < f1)
                    .ToList();

                var currentBudgetExtra = frameTrades.LastOrDefault()?.BudgetExtra ?? lastBudgetExtra;
                var tradeFactor = 1; // TradeCountFactor(frameTrades);
                var fitness = tradeFactor * (currentBudgetExtra - lastBudgetExtra);
                if (fitness < minFitness)
                {
                    minFitness = fitness;
                }
                lastBudgetExtra = currentBudgetExtra;
            }

            return minFitness;
        }

        public LevelsStrategyChromosome GetAdamChromosome() => new();

        public double GetCenterPrice(double price, double asset, double budget, double currency)
        {
            return price;
        }

        public double GetSize(double price, double dir, double asset, double budget, double currency)
        {
            double size;
            var levelBudget = budget * _maxLevelBudget;
            if (!_levels.Any())
            {
                size = levelBudget * _initialBetDistance;

                // need to indicate sell in case the price grows, but we need to buy
                if (dir != 0 && Math.Sign(dir) != Math.Sign(size)) size *= -1;
            }
            else if (dir > 0) // buy
            {
                var candidates = _levels.Where(x => price <= x.Range.Max && price >= x.Range.Min).ToList();
                if (candidates.Any())
                {
                    var match = candidates.FirstOrDefault(x => price < x.Anchor);
                    size = match == null ? 0 : Math.Max(0d, (levelBudget / (match.Anchor - match.Range.Min) * (match.Anchor - price)) - match.Asset);
                }
                else
                {
                    // Insert new level
                    var bottom = _levels.LastOrDefault(x => price < x.Range.Min);
                    if (bottom == null)
                    {
                        //size = 0; // do not buy on higher level
                        var top = _levels.First();
                        var max = top.Range.Max;
                        while (price > max)
                        {
                            max *= 1 + _levelRange;
                        }
                        var anchor = max / (1 + (_levelRange * 0.5d));
                        size = price >= anchor ? 0 : -levelBudget * ((price - anchor) / anchor);
                    }
                    else
                    {
                        var min = bottom.Range.Min;
                        while (price < min)
                        {
                            min /= 1 + _levelRange;
                        }
                        var anchor = min * (1 + (_levelRange * 0.5d));
                        size = price >= anchor ? 0 : -levelBudget * ((price - anchor) / anchor);
                    }
                }
            }
            else
            {
                size = 0;
                foreach (var level in _levels.Where(x => price > x.Anchor))
                {
                    size -= level.Asset * Math.Min(1d, (price - level.Anchor) / level.Anchor);
                }
            }

            return size;
        }

        public void OnTrade(double price, double asset, double size, double currency)
        {
            //todo: size can be slightly off due to fee, compared to calculated

            if (size > 0)
            {
                // Buy -> update existing level or create a single level
                var level = _levels.Where(x => price <= x.Anchor && price >= x.Range.Min).FirstOrDefault();
                if (level == null)
                {
                    var bottom = _levels.LastOrDefault(x => price < x.Range.Min);
                    if (bottom == null)
                    {
                        // new level: anchor should be at [_initialBetDistance %] above current price
                        var anchor = price * (1 + (_initialBetDistance * _levelRange * 0.5));
                        var min = anchor / ((_levelRange * 0.5) + 1);
                        var max = min * (1 + _levelRange);
                        _levels.Insert(0, new Level(anchor, new Range(min, max), price * size, price, size));
                    }
                    else
                    {
                        var index = _levels.IndexOf(bottom);
                        var max = bottom.Range.Max;
                        var min = bottom.Range.Min;
                        while (price < min)
                        {
                            max = min;
                            min /= 1 + _levelRange;
                        }
                        var anchor = min * (1 + (_levelRange * 0.5d));
                        _levels.Insert(index + 1, new Level(anchor, new Range(min, max), price * size, price, size));
                    }
                }
                else
                {
                    var ep = level.Ep + price * size;
                    var newAsset = level.Asset + size;
                    _levels[_levels.IndexOf(level)] = level with
                    {
                        Asset = newAsset,
                        Ep = ep,
                        Enter = ep / newAsset
                    };
                }
            }
            else if (size < 0)
            {
                // Sell
                var remainingSize = -size;
                var first = _levels.FirstOrDefault(x => price >= x.Anchor);
                foreach (var level in _levels.Where(x => price >= x.Anchor).Reverse().ToList())
                {
                    var currentSize = Math.Min(level.Asset, remainingSize);
                    var profit = (price - level.Enter) * currentSize * _mitigationStrength;

                    Level toRedistribute = null;
                    while (profit > 0 && (toRedistribute = _levels.FirstOrDefault(x => price < x.Range.Min)) != null
                        && _levels.IndexOf(level) >= _mitigationLevel)
                    {
                        var belowAnchor = toRedistribute.Anchor / (1 + _levelRange);
                        if (belowAnchor < first.Range.Max)
                        {
                            // Do not redistribute levels below current price
                            break;
                        }

                        var diffPrice = toRedistribute.Enter - belowAnchor;
                        var coverableAssets = profit / diffPrice;
                        var covered = Math.Min(coverableAssets, toRedistribute.Asset);                        

                        OnTrade(belowAnchor, asset, covered, currency);

                        if (coverableAssets >= toRedistribute.Asset)
                        {
                            profit -= diffPrice * covered;
                            _levels.Remove(toRedistribute);
                        }
                        else
                        {
                            var newAsset = toRedistribute.Asset - covered;
                            var ep = toRedistribute.Ep / toRedistribute.Asset * newAsset;
                            _levels[_levels.IndexOf(toRedistribute)] = toRedistribute with
                            {
                                Asset = newAsset,
                                Ep = ep,
                                Enter = ep / newAsset
                            };
                            break;
                        }
                    }

                    if (remainingSize >= level.Asset)
                    {
                        remainingSize -= currentSize;
                        _levels.Remove(level);
                    }
                    else
                    {
                        var newAsset = level.Asset - currentSize;
                        var ep = level.Ep / level.Asset * newAsset;
                        _levels[_levels.IndexOf(level)] = level with
                        {
                            Asset = newAsset,
                            Ep = ep,
                            Enter = ep / newAsset
                        };
                        break;
                    }
                }
            }
        }
    }
}