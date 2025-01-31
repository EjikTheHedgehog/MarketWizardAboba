using ExileCore2.Shared.Interfaces;
using ExileCore2.Shared.Nodes;

namespace MarketWizard;

public class MarketWizardSettings : ISettings
{
    //Mandatory setting to allow enabling/disabling your plugin
    public ToggleNode Enable { get; set; } = new ToggleNode(false);

    public RangeNode<float> GraphPadding { get; set; } = new RangeNode<float>(20, 0, 100);
    public RangeNode<float> GraphHeight { get; set; } = new RangeNode<float>(100, 0, 1000);
    public RangeNode<int> MaxSpreadDepth { get; set; } = new RangeNode<int>(100, 1, 10000);
    public ToggleNode EnableProfitPanel { get; set; } = new ToggleNode(true);
}