using Robust.Shared.Serialization;
using System;
using System.Collections.Generic;

namespace Content.Shared.CriminalRecords;

[Serializable, NetSerializable]
public struct SpaceLawCrime
{
    public string Name;
    public int BrigTime; // minutes
    public int Fine; // credits
    public string Category; // Minor, Medium, Major, Capital

    public SpaceLawCrime(string name, int brigTime, int fine, string category)
    {
        Name = name;
        BrigTime = brigTime;
        Fine = fine;
        Category = category;
    }
}

public static class SpaceLaw
{
    public static readonly List<SpaceLawCrime> Crimes = new()
    {
        // Minor
        new SpaceLawCrime("Trespassing", 2, 100, "Minor"),
        new SpaceLawCrime("Vandalism", 2, 150, "Minor"),
        new SpaceLawCrime("Petty Theft", 3, 200, "Minor"),
        new SpaceLawCrime("Neglect of Duty", 3, 200, "Minor"),
        new SpaceLawCrime("Public Nuisance", 2, 100, "Minor"),
        new SpaceLawCrime("Possession of Contraband", 3, 250, "Minor"),

        // Medium
        new SpaceLawCrime("Assault", 5, 400, "Medium"),
        new SpaceLawCrime("Grand Theft", 5, 500, "Medium"),
        new SpaceLawCrime("Resisting Arrest", 4, 300, "Medium"),
        new SpaceLawCrime("Assault on an Officer", 7, 600, "Medium"),
        new SpaceLawCrime("Possession of Weapons", 6, 500, "Medium"),
        new SpaceLawCrime("Fraud/Forgery", 5, 400, "Medium"),

        // Major
        new SpaceLawCrime("Manslaughter", 10, 800, "Major"),
        new SpaceLawCrime("Sabotage", 10, 1000, "Major"),
        new SpaceLawCrime("Riot/Incitement", 8, 800, "Major"),
        new SpaceLawCrime("Possession of Restricted Gear", 10, 1000, "Major"),
        new SpaceLawCrime("Attempted Murder", 12, 1200, "Major"),
        new SpaceLawCrime("Conspiracy", 8, 800, "Major"),

        // Capital
        new SpaceLawCrime("Murder", 15, 2000, "Capital"),
        new SpaceLawCrime("Mutiny/Treason", 15, 2500, "Capital"),
        new SpaceLawCrime("Grand Sabotage", 15, 2500, "Capital"),
        new SpaceLawCrime("Terrorism", 15, 3000, "Capital")
    };
}
