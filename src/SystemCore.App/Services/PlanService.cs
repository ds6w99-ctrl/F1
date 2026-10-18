using SystemCore.Core.Licensing;

namespace SystemCore.App.Services;

public interface IPlanService
{
    PlanTier Tier { get; }
    bool CanUse(Feature feature);
    // Raised when the plan changes so the UI can re-render gates in place.
    event EventHandler? Changed;
}

// Plan state for the shell. The tier is resolved once at startup; a build with no licence data is
// Free, so an unlicensed copy can never silently unlock Pro features. There is no hidden override:
// the property is settable only through the upgrade path, which today is a no-op that the UI uses
// to open the upgrade entry point.
public sealed class PlanService : IPlanService
{
    private PlanTier tier;
    public PlanService(PlanTier initial = PlanTier.Free) => tier = initial;
    public PlanTier Tier => tier;
    public bool CanUse(Feature feature) => FeaturePlans.IsAvailable(feature, tier);
    public event EventHandler? Changed;
    public void SetTier(PlanTier value)
    {
        if (tier == value) return;
        tier = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
