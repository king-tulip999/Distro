using Core.Config;

namespace Core.Hardware;

internal interface IFakeHardware
{
    (HardwareSnapshot, HardwareCatalog) Step(FanSettings settings);

    SensorTree Tree(HardwareSnapshot hw, HardwareCatalog catalog);

    void SpinUp(string id);

    void SetOverride(string id, float percent);

    void ClearOverride(string id);
}
