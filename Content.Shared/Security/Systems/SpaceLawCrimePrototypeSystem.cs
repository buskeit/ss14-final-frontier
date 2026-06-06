using System.Linq;
using Robust.Shared.Prototypes;

namespace Content.Shared.Security.Systems;

public sealed class SpaceLawCrimePrototypeSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    public override void Initialize()
    {
        base.Initialize();
        ReloadCrimes();
        _prototype.PrototypesReloaded += OnPrototypesReloaded;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _prototype.PrototypesReloaded -= OnPrototypesReloaded;
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<SpaceLawCrimePrototype>())
            ReloadCrimes();
    }

    private void ReloadCrimes()
    {
        SpaceLaw.Crimes.Clear();

        foreach (var crime in _prototype.EnumeratePrototypes<SpaceLawCrimePrototype>()
                     .OrderBy(crime => crime.Order)
                     .ThenBy(crime => crime.Category)
                     .ThenBy(crime => crime.Name))
        {
            SpaceLaw.Crimes.Add(new SpaceLawCrime(crime.Name, crime.Category, crime.BrigTime, crime.Fine));
        }
    }
}
