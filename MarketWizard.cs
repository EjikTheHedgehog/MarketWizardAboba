using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using ExileCore2;
using ExileCore2.PoEMemory.Models;
using ExileCore2.Shared.Helpers;
using ImGuiNET;
using MoreLinq;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;

namespace MarketWizard;

public class MarketWizard : BaseSettingsPlugin<MarketWizardSettings>
{
    private Func<BaseItemType, double> _getNinjaValue;
    private int _spread = 1;
    
    private DateTime _lastDivExRate = DateTime.MinValue;
    private double _divExRate = 0;
    private DateTime _lastChaosDivRate = DateTime.MinValue;
    private double _chaosDivRate = 0;
    private Dictionary<string, ItemPriceData> _itemPrices = new();
    private Dictionary<string, Dictionary<string, CurrencyRatio>> _currencyRatios = new();
    private DateTime _lastRatioUpdate = DateTime.MinValue;
    private bool _showDivineInChaos = false;

    private class ItemPriceData
    {
        public DateTime LastUpdate { get; set; }
        public double PriceInDivine { get; set; }
        public double PriceInExalted { get; set; }
        public double PriceInChaos { get; set; }
    }

    private class CurrencyRatio
    {
        public DateTime LastUpdate { get; set; }
        public double Ratio { get; set; }
        public int Volume { get; set; }
    }

    public override bool Initialise()
    {
        return true;
    }

    private void UpdateCurrencyRates(BaseItemType wanted, BaseItemType offered, float ratio)
    {
        if ((wanted.BaseName == "Divine Orb" && offered.BaseName == "Exalted Orb") ||
            (wanted.BaseName == "Exalted Orb" && offered.BaseName == "Divine Orb"))
        {
            _lastDivExRate = DateTime.Now;
            _divExRate = wanted.BaseName == "Divine Orb" ? ratio : 1 / ratio;
        }

        if ((wanted.BaseName == "Divine Orb" && offered.BaseName == "Chaos Orb") ||
            (wanted.BaseName == "Chaos Orb" && offered.BaseName == "Divine Orb"))
        {
            _lastChaosDivRate = DateTime.Now;
            _chaosDivRate = wanted.BaseName == "Divine Orb" ? ratio : 1 / ratio;
        }
    }

    private void UpdateItemPrice(BaseItemType item, BaseItemType currency, float ratio)
    {
        if (item == null || currency == null) return;

        var itemName = item.BaseName;
        if (!_itemPrices.ContainsKey(itemName))
        {
            _itemPrices[itemName] = new ItemPriceData();
        }

        var priceData = _itemPrices[itemName];
        priceData.LastUpdate = DateTime.Now;

        switch (currency.BaseName)
        {
            case "Divine Orb":
                priceData.PriceInDivine = ratio;
                if (_divExRate > 0)
                    priceData.PriceInExalted = ratio * _divExRate;
                if (_chaosDivRate > 0)
                    priceData.PriceInChaos = ratio * _chaosDivRate;
                break;
            case "Exalted Orb":
                priceData.PriceInExalted = ratio;
                if (_divExRate > 0)
                {
                    priceData.PriceInDivine = ratio / _divExRate;
                    if (_chaosDivRate > 0)
                        priceData.PriceInChaos = priceData.PriceInDivine * _chaosDivRate;
                }
                break;
            case "Chaos Orb":
                priceData.PriceInChaos = ratio;
                if (_chaosDivRate > 0)
                {
                    priceData.PriceInDivine = ratio / _chaosDivRate;
                    if (_divExRate > 0)
                        priceData.PriceInExalted = priceData.PriceInDivine * _divExRate;
                }
                break;
        }
    }

    private void UpdateCurrencyRatio(BaseItemType wanted, BaseItemType offered, float ratio, int volume)
    {
        if (wanted == null || offered == null) return;

        if (!_currencyRatios.ContainsKey(wanted.BaseName))
            _currencyRatios[wanted.BaseName] = new Dictionary<string, CurrencyRatio>();

        _currencyRatios[wanted.BaseName][offered.BaseName] = new CurrencyRatio
        {
            LastUpdate = DateTime.Now,
            Ratio = ratio,
            Volume = volume
        };

        if (!_currencyRatios.ContainsKey(offered.BaseName))
            _currencyRatios[offered.BaseName] = new Dictionary<string, CurrencyRatio>();

        _currencyRatios[offered.BaseName][wanted.BaseName] = new CurrencyRatio
        {
            LastUpdate = DateTime.Now,
            Ratio = 1 / ratio,
            Volume = volume
        };

        _lastRatioUpdate = DateTime.Now;
    }

    private (double profitPercent, string betterCurrency) CalculatePriceProfit(ItemPriceData priceData)
    {
        if (priceData == null || _divExRate <= 0 || _chaosDivRate <= 0) 
            return (0, "Unknown");

        var divinePrice = priceData.PriceInDivine;
        var exaltedPriceInDivine = priceData.PriceInExalted / _divExRate;
        var chaosPriceInDivine = priceData.PriceInChaos / _chaosDivRate;

        var prices = new[]
        {
            (Currency: "Divine", Price: divinePrice),
            (Currency: "Exalted", Price: exaltedPriceInDivine),
            (Currency: "Chaos", Price: chaosPriceInDivine)
        };

        var minPrice = prices.MinBy(x => x.Price);
        var maxPrice = prices.MaxBy(x => x.Price);

        var profitPercent = ((maxPrice.Price - minPrice.Price) / minPrice.Price) * 100;
        
        return profitPercent switch
        {
            > 0 => (Math.Abs(profitPercent), minPrice.Currency),
            _ => (0, "Equal")
        };
    }

    private (int profitAmount, string buyCurrency) CalculateCurrencyProfit(string currencyName)
    {
        if (!_currencyRatios.ContainsKey(currencyName)) 
            return (0, "No data");

        var divineRatio = GetRatioOrDefault(currencyName, "Divine Orb");
        var exaltedRatio = GetRatioOrDefault(currencyName, "Exalted Orb"); 
        var chaosRatio = GetRatioOrDefault(currencyName, "Chaos Orb");

        if (divineRatio <= 0 || exaltedRatio <= 0 || chaosRatio <= 0 || 
            _divExRate <= 0 || _chaosDivRate <= 0)
            return (0, "No data");

        var (profit, buyCurrency) = GetBestProfitCurrency(divineRatio, exaltedRatio, chaosRatio);
        return profit < 1 ? (0, "No profit") : ((int)profit, buyCurrency);
    }

    public override void AreaChange(AreaInstance area)
    {
    }

    public override void Tick()
    {
        _getNinjaValue = GameController.PluginBridge.GetMethod<Func<BaseItemType, double>>("NinjaPrice.GetBaseItemTypeValue");
    }

    private double? GetNinjaRatio(BaseItemType wantedItem, BaseItemType offeredItem)
    {
        return _getNinjaValue?.Invoke(wantedItem) / _getNinjaValue?.Invoke(offeredItem);
    }

    public override void Render()
    {
        if (GameController.IngameState.IngameUi.CurrencyExchangePanel is not { IsVisible: true } panel)
        {
            return;
        }

        if (panel is { WantedItemType: { } wantedItemType, OfferedItemType: { } offeredItemType })
        {
            var firstOffer = panel?.OfferedItemStock?
                .FirstOrDefault(x => x != null && x.Give != 0 && x.Get != 0);

            float? firstOfferedRatio = null;
            if (firstOffer != null && firstOffer.Give > 0)
            {
                firstOfferedRatio = firstOffer.Get / (float)firstOffer.Give;
            }
            
            if (firstOfferedRatio.HasValue)
            {
                UpdateCurrencyRates(wantedItemType, offeredItemType, firstOfferedRatio.Value);
                
                if (wantedItemType.BaseName != "Divine Orb" && wantedItemType.BaseName != "Exalted Orb")
                {
                    if (offeredItemType.BaseName == "Divine Orb" || offeredItemType.BaseName == "Exalted Orb")
                    {
                        UpdateItemPrice(wantedItemType, offeredItemType, firstOfferedRatio.Value);
                    }
                }
            }

            if (firstOffer != null && firstOffer.Give > 0)
            {
                float ratio = firstOffer.Get / (float)firstOffer.Give;
                int volume = firstOffer.ListedCount;
                UpdateCurrencyRatio(wantedItemType, offeredItemType, ratio, volume);
            }

            if (ImGui.Begin("StockWindow"))
            {
                var offeredStock = panel.OfferedItemStock.Select(x => (x.Give, x.Get, Ratio: x.Get / (float)x.Give, ListedCount: x.ListedCount * x.Give / (float)x.Get))
                    .Where(x => x.Get != 0 && x.Give != 0 && x.ListedCount > 0).ToList();
                var wantedStock = panel.WantedItemStock.Select(x => (x.Give, x.Get, Ratio: x.Give / (float)x.Get, x.ListedCount))
                    .Where(x => x.Get != 0 && x.Give != 0 && x.ListedCount > 0).ToList();
                var leftPoints = offeredStock
                    .OrderByDescending(x => x.Ratio)
                    .Aggregate((0f, new List<(float Ratio, float ListedCount)>().AsEnumerable()), (a, x) =>
                        (x.ListedCount + a.Item1, a.Item2.Append((x.Ratio, x.ListedCount + a.Item1))))
                    .Item2.Reverse().ToList();
                var rightPoints = wantedStock
                    .OrderBy(x => x.Ratio)
                    .Aggregate((0, new List<(float Ratio, float ListedCount)>().AsEnumerable()), (a, x) =>
                        (x.ListedCount + a.Item1, a.Item2.Append((x.Ratio, x.ListedCount + a.Item1))))
                    .Item2.ToList();
                var wantedItemStockRest = panel.WantedItemStock.FirstOrDefault(x => x.Give == 0 && x.Get == 0);
                var offeredItemStockRest = panel.OfferedItemStock.FirstOrDefault(x => x.Give == 0 && x.Get == 0);
                if (leftPoints.Any() || leftPoints.Any())
                {
                    var trueLeftmostX = leftPoints.Concat(rightPoints).First().Ratio;
                    var trueRightmostX = leftPoints.Concat(rightPoints).Last().Ratio;
                    var expansionCoefficient = 0.05f;
                    var expansionAmount = (trueRightmostX - trueLeftmostX) * expansionCoefficient;
                    if (rightPoints.Any() && wantedItemStockRest is { ListedCount: > 0 and var restRightListed })
                    {
                        rightPoints.Add((trueRightmostX + expansionAmount, restRightListed + rightPoints.Last().ListedCount));
                    }

                    if (leftPoints.Any() && offeredItemStockRest is { ListedCount: > 0 and var restLeftListed })
                    {
                        leftPoints.Insert(0, (trueLeftmostX - expansionAmount, restLeftListed / leftPoints.First().Ratio + leftPoints.First().ListedCount));
                    }

                    var (leftmostX, rightmostX) = (trueLeftmostX - expansionAmount * 2, trueRightmostX + expansionAmount * 2);

                    var graphSizeX = ImGui.GetContentRegionAvail().X - Settings.GraphPadding.Value * 2;
                    var graphSizeY = Settings.GraphHeight.Value;

                    float RatioToU(double ratio) => (float)((ratio - leftmostX) / (rightmostX - leftmostX));
                    Vector2 UvToXy(Vector2 v) => v * new Vector2(graphSizeX, -graphSizeY) + new Vector2(0, graphSizeY);
                    Vector2 UvToXyBottom(Vector2 v) => v * new Vector2(graphSizeX, 0) + new Vector2(0, graphSizeY);

                    var topY = Math.Max(leftPoints.FirstOrDefault().ListedCount, rightPoints.LastOrDefault().ListedCount);
                    var leftPointsUv = leftPoints.Select(x => new Vector2(RatioToU(x.Ratio), MathF.Log(x.ListedCount + 1) / MathF.Log(topY)))
                        .Prepend(new Vector2(0, 0))
                        .ToList();
                    var rightPointsUv = rightPoints.Select(x => new Vector2(RatioToU(x.Ratio), MathF.Log(x.ListedCount + 1) / MathF.Log(topY)))
                        .Append(new Vector2(1, 0))
                        .ToList();

                    var leftPointXyPairs = leftPointsUv.Pairwise((l, r) => new[]
                    {
                        UvToXy(r with { X = l.X }),
                        UvToXy(r)
                    }).ToList();
                    var cursorScreenPos = ImGui.GetCursorScreenPos() + new Vector2(Settings.GraphPadding.Value, 0);

                    Span<Vector2> leftPointsXy = leftPointXyPairs.SelectMany(x => x)
                        .Concat(leftPointsUv.AsEnumerable().Reverse().Select(UvToXyBottom))
                        .Select(x => x + cursorScreenPos)
                        .ToArray();
                    var rightPointXyPairs = rightPointsUv.Pairwise((l, r) => new[]
                    {
                        UvToXy(l),
                        UvToXy(l with { X = r.X })
                    }).ToList();
                    Span<Vector2> rightPointsXy = rightPointXyPairs.SelectMany(x => x)
                        .Concat(rightPointsUv.AsEnumerable().Reverse().Select(UvToXyBottom))
                        .Select(x => x + cursorScreenPos)
                        .ToArray();

                    var drawList = ImGui.GetWindowDrawList();
                    foreach (var pair in leftPointXyPairs)
                    {
                        drawList.AddRectFilled(pair[0] + cursorScreenPos, pair[1] with { Y = graphSizeY } + cursorScreenPos,
                            (Color.Red.ToImguiVec4(60).ToColor()).ToImgui());
                    }

                    foreach (var pair in rightPointXyPairs)
                    {
                        drawList.AddRectFilled(pair[0] + cursorScreenPos, pair[1] with { Y = graphSizeY } + cursorScreenPos,
                            (Color.Green.ToImguiVec4(60).ToColor()).ToImgui());
                    }

                    drawList.AddPolyline(ref leftPointsXy[0], leftPointsXy.Length, Color.Red.ToImgui(), ImDrawFlags.Closed, 1);
                    drawList.AddPolyline(ref rightPointsXy[0], rightPointsXy.Length, Color.Green.ToImgui(), ImDrawFlags.Closed, 1);


                    var lineDict = new Dictionary<int, float>();
                    var numberSet = new HashSet<string>();

                    float GetLineHeight(int key)
                    {
                        if (lineDict.TryGetValue(key, out var height))
                        {
                            return height;
                        }

                        height = lineDict.Count * ImGui.GetTextLineHeight() + cursorScreenPos.Y + graphSizeY +
                                 (ImGui.GetTextLineHeightWithSpacing() - ImGui.GetTextLineHeight()) / 2;
                        lineDict[key] = height;
                        return height;
                    }

                    var leftString = ((double)trueLeftmostX).FormatNumber(2, 0.2);
                    if (numberSet.Add(leftString))
                    {
                        DrawTextMiddle(drawList, leftString, new Vector2(cursorScreenPos.X, GetLineHeight(0)));
                    }

                    var rightString = ((double)trueRightmostX).FormatNumber(2, 0.2);
                    if (numberSet.Add(rightString))
                    {
                        DrawTextMiddle(drawList, rightString, new Vector2(cursorScreenPos.X + graphSizeX, GetLineHeight(0)));
                    }

                    if (leftPoints.Any())
                    {
                        double ratio = leftPoints.Last().Ratio;
                        var leftMString = ratio.FormatNumber(2, 0.2);
                        if (numberSet.Add(leftMString))
                        {
                            DrawTextMiddle(drawList, leftMString, new Vector2(cursorScreenPos.X + UvToXy(new Vector2(RatioToU(ratio), 0)).X, GetLineHeight(1)));
                        }
                    }

                    if (rightPoints.Any())
                    {
                        double ratio = rightPoints.First().Ratio;
                        var rightMString = ratio.FormatNumber(2, 0.2);
                        if (numberSet.Add(rightMString))
                        {
                            DrawTextMiddle(drawList, rightMString, new Vector2(cursorScreenPos.X + UvToXy(new Vector2(RatioToU(ratio), 0)).X, GetLineHeight(1)));
                        }
                    }

                    if (GetNinjaRatio(wantedItemType, offeredItemType) is { } ninjaRatio)
                    {
                        var ninjaU = RatioToU((float)ninjaRatio);
                        if (ninjaU >= 0 && ninjaU <= 1)
                        {
                            var ninjaUvLow = new Vector2(ninjaU, 0);
                            var ninjaUvHigh = new Vector2(ninjaU, 0.3f);
                            drawList.AddLine(UvToXy(ninjaUvLow) + cursorScreenPos, UvToXy(ninjaUvHigh) + cursorScreenPos, Color.White.ToImgui(), 3);
                            var ninjaString = ninjaRatio.FormatNumber(2, 0.2);
                            if (numberSet.Add(ninjaString))
                            {
                                DrawTextMiddle(drawList, ninjaString, new Vector2(cursorScreenPos.X + UvToXy(ninjaUvLow).X, GetLineHeight(2)));
                            }
                        }
                    }

                    ImGui.Dummy(new Vector2(graphSizeX + Settings.GraphPadding.Value * 2,
                        graphSizeY + ImGui.GetTextLineHeightWithSpacing() + (lineDict.Count - 1) * ImGui.GetTextLineHeight()));
                }

                if (ImGui.BeginTable("orderbook", 2))
                {
                    ImGui.TableSetupColumn("Offered item listings");
                    ImGui.TableSetupColumn("Wanted item listings");
                    ImGui.TableHeadersRow();
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    if (ImGui.BeginTable("offeredStock", 2))
                    {
                        ImGui.TableSetupColumn("Ratio");
                        ImGui.TableSetupColumn("Count (in wanted items)");
                        ImGui.TableHeadersRow();
                        foreach (var offeredStockItem in offeredStock)
                        {
                            ImGui.TableNextRow();
                            ImGui.TableNextColumn();
                            ImGui.Text($"{((double)offeredStockItem.Ratio).FormatNumber(2, 0.2)}");
                            ImGui.TableNextColumn();
                            ImGui.Text($"{offeredStockItem.ListedCount}");
                        }

                        if (offeredItemStockRest is { ListedCount: > 0 })
                        {
                            ImGui.TableNextRow();
                            ImGui.TableNextColumn();
                            ImGui.Text($"<{((double)offeredStock.Last().Ratio).FormatNumber(2, 0.2)}");
                            ImGui.TableNextColumn();
                            ImGui.Text($"{offeredItemStockRest.ListedCount / offeredStock.Last().Ratio:F1}");
                        }

                        ImGui.EndTable();
                    }

                    ImGui.TableNextColumn();

                    if (ImGui.BeginTable("wantedStock", 2))
                    {
                        ImGui.TableSetupColumn("Ratio");
                        ImGui.TableSetupColumn("Count");
                        ImGui.TableHeadersRow();
                        foreach (var wantedStockItem in wantedStock)
                        {
                            ImGui.TableNextRow();
                            ImGui.TableNextColumn();
                            ImGui.Text($"{((double)wantedStockItem.Ratio).FormatNumber(2, 0.2)}");
                            ImGui.TableNextColumn();
                            ImGui.Text($"{wantedStockItem.ListedCount}");
                        }

                        if (wantedItemStockRest is { ListedCount: > 0 })
                        {
                            ImGui.TableNextRow();
                            ImGui.TableNextColumn();
                            ImGui.Text($">{((double)wantedStock.Last().Ratio).FormatNumber(2, 0.2)}");
                            ImGui.TableNextColumn();
                            ImGui.Text($"{wantedItemStockRest.ListedCount}");
                        }

                        ImGui.EndTable();
                    }

                    ImGui.EndTable();
                }

                ImGui.Text($"Total {wantedItemType.BaseName} volume: {panel.WantedItemStock.Sum(x => x.ListedCount)}");
                ImGui.Text($"Total {offeredItemType.BaseName} volume: {panel.OfferedItemStock.Sum(x => x.ListedCount)}");
                ImGui.SliderInt("Spread depth", ref _spread, 1, Settings.MaxSpreadDepth);
                ImGui.Text($"Spread: {((wantedStock.SkipWhile((w, i) => wantedStock.Take(i + 1).Sum(ww => ww.ListedCount) < _spread).FirstOrDefault().Ratio /
                    offeredStock.SkipWhile((o, i) => offeredStock.Take(i + 1).Sum(oo => oo.ListedCount) < _spread).FirstOrDefault().Ratio - 1) * 100) switch {
                    < 0 => "Unknown",
                    float.NaN => "Unknown",
                    var x => x.ToString("F0") + "%%"
                }}");
            }
        }

        DrawCurrencyInfo();
    }

    private void DrawCurrencyInfo()
    {
        if (!Settings.EnableProfitPanel || !_currencyRatios.Any()) 
            return;

        ImGuiWindowFlags windowFlags = 
            ImGuiWindowFlags.NoScrollWithMouse | 
            ImGuiWindowFlags.NoScrollbar;

        var initialSize = new Vector2(600, 400);
        ImGui.SetNextWindowSize(initialSize, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(400, 200), new Vector2(float.MaxValue, float.MaxValue));

        if (ImGui.Begin("Currency Exchange Profits", windowFlags))
        {
            if (ImGui.Button("Clear Cache"))
            {
                ClearCache();
            }
            ImGui.SameLine();

            if (ImGui.Button(_showDivineInChaos ? "Show in Ex" : "Show in Chaos"))
            {
                _showDivineInChaos = !_showDivineInChaos;
            }
            ImGui.SameLine();
            
            // Define colors
            var warningColor = new Vector4(1.0f, 1.0f, 0.0f, 1.0f); // Yellow
            var validColor = new Vector4(0.498f, 0.788f, 0.576f, 1.0f); // #7fc993

            // Divine/Ex rate display
            ImGui.TextColored(
                _divExRate <= 0 ? warningColor : validColor,
                $"Divine/Ex rate: {(_divExRate <= 0 ? "Unknown" : _divExRate.ToString("F2"))}");
            
            ImGui.SameLine();
            ImGui.Text("|");
            ImGui.SameLine();

            // Divine/Chaos rate display
            ImGui.TextColored(
                _chaosDivRate <= 0 ? warningColor : validColor,
                $"Divine/Chaos rate: {(_chaosDivRate <= 0 ? "Unknown" : _chaosDivRate.ToString("F2"))}");

            ImGui.Separator();

            var tableFlags = ImGuiTableFlags.Resizable | 
                            ImGuiTableFlags.Reorderable | 
                            ImGuiTableFlags.Hideable | 
                            ImGuiTableFlags.Sortable | 
                            ImGuiTableFlags.BordersV |
                            ImGuiTableFlags.ScrollY;

            float tableHeight = ImGui.GetContentRegionAvail().Y;

            if (ImGui.BeginTable("CurrencyRatios", 5, tableFlags, new Vector2(0, tableHeight)))
            {
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableSetupColumn("Currency\nName", ImGuiTableColumnFlags.DefaultSort);
                ImGui.TableSetupColumn("Divine\nRate");
                ImGui.TableSetupColumn("Exalted\nRate");
                ImGui.TableSetupColumn("Chaos\nRate");
                ImGui.TableSetupColumn("Profit");
                ImGui.TableHeadersRow();

                foreach (var currency in _currencyRatios.Keys
                    .Where(k => k != "Divine Orb" && k != "Exalted Orb" && k != "Chaos Orb")
                    .OrderByDescending(k => CalculateCurrencyProfit(k).profitAmount))
                {
                    var divineRatio = _currencyRatios[currency].GetValueOrDefault("Divine Orb")?.Ratio ?? 0;
                    var exaltedRatio = _currencyRatios[currency].GetValueOrDefault("Exalted Orb")?.Ratio ?? 0;
                    var chaosRatio = _currencyRatios[currency].GetValueOrDefault("Chaos Orb")?.Ratio ?? 0;

                    if (divineRatio <= 0 && exaltedRatio <= 0 && chaosRatio <= 0) continue;

                    ImGui.TableNextRow();
                    
                    ImGui.TableNextColumn();
                    ImGui.TextWrapped(currency);

                    ImGui.TableNextColumn();
                    DisplayCurrencyRate(currency, divineRatio);

                    ImGui.TableNextColumn();
                    if (exaltedRatio > 0)
                    {
                        var divineEquiv = exaltedRatio / _divExRate;
                        ImGui.Text($"1:{exaltedRatio:F1}\n(1:{divineEquiv:F1} div)");
                    }
                    else
                    {
                        ImGui.Text("-");
                    }

                    ImGui.TableNextColumn();
                    if (chaosRatio > 0)
                    {
                        var divineEquiv = chaosRatio / _chaosDivRate;
                        ImGui.Text($"1:{chaosRatio:F1}\n(1:{divineEquiv:F1} div)");
                    }
                    else
                    {
                        ImGui.Text("-");
                    }

                    ImGui.TableNextColumn();
                    var (profit, buyCurrency) = CalculateCurrencyProfit(currency);
                    if (profit > 0)
                    {
                        var color = profit > 5 ? Color.LightGreen : Color.White;
                        ImGui.TextColored(color.ToImguiVec4(), $"{profit}ex\nBuy with {buyCurrency}");
                    }
                    else
                    {
                        ImGui.TextColored(Color.Gray.ToImguiVec4(), "No profit");
                    }
                }

                ImGui.EndTable();
            }
            ImGui.End();
        }
    }

    private void ClearCache()
    {
        var tempDivExRate = _divExRate;
        var tempLastDivExRate = _lastDivExRate;
        var tempChaosDivRate = _chaosDivRate;
        var tempLastChaosDivRate = _lastChaosDivRate;

        _currencyRatios.Clear();
        _itemPrices.Clear();

        _divExRate = tempDivExRate;
        _lastDivExRate = tempLastDivExRate;
        _chaosDivRate = tempChaosDivRate;
        _lastChaosDivRate = tempLastChaosDivRate;
    }

    private void DrawTextMiddle(ImDrawListPtr drawList, string text, Vector2 position)
    {
        var textSizeX = ImGui.CalcTextSize(text).X;
        drawList.AddText(position - new Vector2(textSizeX / 2, 0), Color.White.ToImgui(), text);
    }

    private void DisplayCurrencyRate(string currency, double ratio, string prefix = "")
    {
        if (ratio <= 0)
        {
            ImGui.Text("-");
            return;
        }

        var (convRate, label) = _showDivineInChaos 
            ? (_chaosDivRate, "c")
            : (_divExRate, "ex");

        var convertedEquiv = ratio * convRate;
        
        ImGui.Text($"{prefix}1:{ratio:F1}");
        ImGui.Text($"(1:{convertedEquiv:F1} {label})");
    }

    private (double profit, string currency) GetBestProfitCurrency(double divinePrice, double exaltedPrice, double chaosPrice)
    {
        var prices = new[]
        {
            ("Divine", divinePrice),
            ("Exalted", exaltedPrice / _divExRate),
            ("Chaos", chaosPrice / _chaosDivRate)
        };

        var min = prices.MinBy(x => x.Item2);
        var max = prices.MaxBy(x => x.Item2);
        
        var profit = (max.Item2 - min.Item2) * _divExRate;
        return profit > 0 ? (profit, min.Item1) : (0, "Equal");
    }

    private double GetRatioOrDefault(string currency, string target) =>
        _currencyRatios.GetValueOrDefault(currency)?.GetValueOrDefault(target)?.Ratio ?? 0;
}

public static class Extensions
{
    public static string FormatNumber(this double number, int significantDigits, double maxInvertValue = 0, bool forceDecimals = false)
    {
        if (double.IsNaN(number))
        {
            return "NaN";
        }

        if (number == 0)
        {
            return "0";
        }

        if (Math.Abs(number) <= 1e-10)
        {
            return "~0";
        }

        if (Math.Abs(number) < maxInvertValue)
        {
            return $"1/{Math.Round((decimal)(1 / number), 1):#.#}";
        }

        return Math.Round((decimal)number, significantDigits).ToString($"#,##0.{new string(forceDecimals ? '0' : '#', significantDigits)}");
    }
}