using System.Linq;
using Content.Shared.CriminalRecords;
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
        var crimes = _prototype.EnumeratePrototypes<SpaceLawCrimePrototype>()
            .OrderBy(crime => crime.Order)
            .ThenBy(crime => crime.Category)
            .ThenBy(crime => crime.Name)
            .ToArray();

        foreach (var crime in crimes)
        {
            var entry = new SpaceLawCrime(crime.Name, crime.BrigTime, crime.Fine, crime.Category);
            var existingIndex = SpaceLaw.Crimes.FindIndex(existing => existing.Name == crime.Name);

            if (existingIndex >= 0)
            {
                SpaceLaw.Crimes[existingIndex] = entry;
                continue;
            }

            SpaceLaw.Crimes.Add(entry);
        }
    }
}
