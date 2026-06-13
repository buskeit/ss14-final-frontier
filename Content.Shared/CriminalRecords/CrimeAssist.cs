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

public static class CrimeAssistFormatter
{
    public static string FormatSentence(int minutes)
    {
        if (minutes <= -1)
            return "Permanent";

        return FormatDuration(TimeSpan.FromMinutes(minutes));
    }

    public static string FormatDuration(TimeSpan duration)
    {
        if (duration.Ticks < 0)
            return "Permanent";

        double totalSeconds = duration.TotalSeconds;
        if (totalSeconds == 0)
            return "0m";

        // If under 2 minutes (120 seconds), show minutes and seconds
        if (totalSeconds < 120)
        {
            int mins = (int) (totalSeconds / 60);
            int secs = (int) (totalSeconds % 60);

            if (mins > 0 && secs > 0)
                return $"{mins}m {secs}s";
            if (mins > 0)
                return $"{mins}m";
            return $"{secs}s";
        }

        // Otherwise, convert using approximate calendar-style conversion:
        // 1y = 365d, 1mo = 30d, 1d = 24h, 1h = 60m
        long totalMinutes = (long) Math.Round(totalSeconds / 60.0);
        if (totalMinutes == 0)
            return "0m";

        long minutesInHour = 60;
        long minutesInDay = 24 * minutesInHour;
        long minutesInMonth = 30 * minutesInDay;
        long minutesInYear = 365 * minutesInDay;

        long years = totalMinutes / minutesInYear;
        totalMinutes %= minutesInYear;

        long months = totalMinutes / minutesInMonth;
        totalMinutes %= minutesInMonth;

        long days = totalMinutes / minutesInDay;
        totalMinutes %= minutesInDay;

        long hours = totalMinutes / minutesInHour;
        long minsRemaining = totalMinutes % minutesInHour;

        List<string> parts = new();

        if (years > 0)
            parts.Add($"{years}y");
        if (months > 0)
            parts.Add($"{months}mo");
        if (days > 0)
            parts.Add($"{days}d");
        if (hours > 0)
            parts.Add($"{hours}h");
        if (minsRemaining > 0)
            parts.Add($"{minsRemaining}m");

        if (parts.Count >= 2)
            return $"{parts[0]} {parts[1]}";
        if (parts.Count == 1)
            return parts[0];

        return "0m";
    }
}
