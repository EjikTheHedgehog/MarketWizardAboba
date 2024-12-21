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

namespace MarketWizard;

public class MarketWizard : BaseSettingsPlugin<MarketWizardSettings>
{
    private Func<BaseItemType, double> _getNinjaValue;
    private int _spread = 1;

    public override bool Initialise()
    {
        return true;
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
    }

    private void DrawTextMiddle(ImDrawListPtr drawList, string text, Vector2 position)
    {
        var textSizeX = ImGui.CalcTextSize(text).X;
        drawList.AddText(position - new Vector2(textSizeX / 2, 0), Color.White.ToImgui(), text);
    }
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