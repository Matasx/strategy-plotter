﻿using System.Globalization;

var random = new Random();

//var filename = "FTX_DOGE-PERP_03.12.2020_03.12.2021.csv";
//var filename = "FTX_DOGE-PERP_03.12.2021_01.02.2022.csv";
var filename = "KUCOIN_HTR-USDT_10.02.2021_10.02.2022-cut.csv";

var prices = File
    .ReadAllLines(filename)
    .Where(x => !string.IsNullOrWhiteSpace(x))
    .Select(x => double.Parse(x, NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture))
    .ToList();
using var writer = File.CreateText("out.csv");

var trades = SpreadRunner.GenerateTrades(new MMBot.Api.dto.GenTradesRequest
{
    BeginTime = 0,
    Stdev = 95.5,
    Sma = 2,
    Mult = 1,
    Mode = "Together",
    Raise = 113.9,
    Fall = 1.6,
    Cap = 58,
    DynMult = true
}, prices).ToDictionary(x => x.Time / 60000, x => x.Price);

writer.WriteLine("price,trade,size,cost,asset,currency,equity,enter,budget extra");

var strategy = new Test();
var ep = 0d;
var asset = 0d;
const double budget = 10000d;
var currency = budget;
var index = 0;
var reinvest = false;
var budgetExtra = 0d;

foreach (var p in prices)
{
    var size = 0d;
    var price = p;
    var trade = trades.TryGetValue(index, out var genPrice);
    var tradableCurrency = currency - budgetExtra;

    if (trade)
    {
        price = genPrice;
        size = strategy.GetSize(price, asset, budget, tradableCurrency);
    }

    index++;
    var cost = price * size;
    if (cost > tradableCurrency)
    {
        cost = tradableCurrency;
        size = cost / price;
    }
    else if (-size > asset)
    {
        size = -asset;
        cost = price * size;
    }
    currency -= cost;

    if (!reinvest && currency > budget + budgetExtra)
    {
        budgetExtra = currency - budget;
    }

    if (size != 0)
    {
        strategy.OnTrade(price, asset, size);
    }

    var newAsset = asset + size;
    ep = size >= 0 ? ep + cost : (ep / asset) * newAsset;
    asset = newAsset;

    writer.Add(price);
    writer.Add(trade ? price.Ts() : string.Empty);
    writer.Add(size);
    writer.Add(cost);
    writer.Add(asset);
    writer.Add(currency);
    writer.Add(currency + (asset * price));
    writer.Add(asset == 0 ? string.Empty : (ep / asset).Ts());
    writer.Add(budgetExtra, true);
}

class Test
{
    double _ep = 0d;
    double _enter = double.NaN;

    double _minAssetPercOfBudget = 0.001;
    double _initialBetPercOfBudget = 0.04;

    double _maxEnterPriceDistance = 0.05;
    double _powerMult = 0.5;
    double _powerCap = 1;

    static double _angle = 41; //0-90; the higher, the less assets to buy
    static double _angleRad = _angle * Math.PI / 180;
    static double _sqrtTan = Math.Sqrt(Math.Tan(_angleRad));

    double _targetExitPriceDistance = 0.04;
    double _exitPowerMult = 6;

    public double GetSize(double price, double asset, double budget, double currency)
    {
        double size;
        if (double.IsNaN(_enter) || (asset * price) < budget * _minAssetPercOfBudget)
        {
            // initial bet -> buy
            size = (budget * _initialBetPercOfBudget) / price;
        }
        else if (price < _enter)
        {
            // buy to lower enter price

            // https://www.desmos.com/calculator/na4ovcuavg
            // https://www.desmos.com/calculator/rkw80qbgp3
            // a: _ep
            // b: price
            // c: asset
            // d: target angle
            // x: size

            // calculate recommended price based on preference of cost to reduction ratio
            var cost = Math.Sqrt(_ep) / _sqrtTan;
            var candidateSize = cost / price;

            var dist = (_enter - price) / _enter;
            var norm = dist / _maxEnterPriceDistance;
            var power = Math.Min(Math.Pow(norm, 4) * _powerMult, _powerCap);
            var newSize = candidateSize * power;

            return double.IsNaN(newSize) ? 0 : Math.Max(0, newSize);
        }
        else
        {
            // sell?
            var dist = (price - _enter) / price;
            var norm = dist / _targetExitPriceDistance;
            var power = Math.Pow(norm, 4) * _exitPowerMult;
            size = -asset * power;
        }

        return size;
    }

    public void OnTrade(double price, double asset, double size)
    {
        var newAsset = asset + size;
        _ep = size >= 0 ? _ep + (price * size) : (_ep / asset) * newAsset;
        _enter = _ep / newAsset;
    }
}

class HalfHalf
{
    public double GetSize(double price, double asset, double budget, double currency)
    {
        return (((currency / price) + asset) * 0.5) - asset;
    }

    public void OnTrade(double price, double size)
    { }
}
