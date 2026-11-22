using System.Collections.Concurrent;

namespace Core.SimRacing;

internal static class CarBrands
{
    private static readonly (string Part, string Brand)[] Rules =
    {
        ("hpdarx", "hpd"),
        ("acura", "acura"),
        ("amvantage", "aston-martin"), ("amvalkyrie", "aston-martin"), ("astonmartin", "aston-martin"), ("aston martin", "aston-martin"), ("dbr9", "aston-martin"),
        ("audi", "audi"),
        ("bmw", "bmw"),
        ("cadillac", "cadillac"),
        ("caterham", "caterham"),
        ("holden", "holden"),
        ("chevy", "chevrolet"), ("chevrolet", "chevrolet"), ("vette", "chevrolet"), ("camaro", "chevrolet"), ("silverado", "chevrolet"),
        ("impala", "chevrolet"), ("montecarlo", "chevrolet"), ("cruze", "chevrolet"), ("c6r", "chevrolet"),
        ("ferrari", "ferrari"),
        ("ford", "ford"), ("fr500s", "ford"), ("mustang", "ford"), ("fusion", "ford"), ("taurus", "ford"), ("thunderbird", "ford"), ("specracer", "ford"),
        ("honda", "honda"), ("civic", "honda"),
        ("hyundai", "hyundai"),
        ("kia", "kia"),
        ("lamborghini", "lamborghini"),
        ("ligier", "ligier"),
        ("mazda", "mazda"), ("mx5", "mazda"), ("mx-5", "mazda"),
        ("mclaren", "mclaren"),
        ("mercedes", "mercedes"),
        ("nissan", "nissan"),
        ("pontiac", "pontiac"), ("solstice", "pontiac"),
        ("porsche", "porsche"),
        ("radical", "radical"),
        ("renault", "renault"),
        ("rufrt", "ruf"), ("ruf ", "ruf"),
        ("subaru", "subaru"),
        ("toyota", "toyota"), ("camry", "toyota"), ("supra", "toyota"), ("tundra", "toyota"), ("corolla", "toyota"),
        ("volkswagen", "volkswagen"), ("vwbeetle", "volkswagen"), ("vw ", "volkswagen"), ("jetta", "volkswagen"),
        ("williams", "williams"),
        ("dallara", "dallara"),
    };

    private static readonly ConcurrentDictionary<(string Path, string Name), string?> Known = new();

    public static string? Of(string? carPath, string? carName)
    {
        var key = (carPath ?? "", carName ?? "");
        if (Known.TryGetValue(key, out var brand)) return brand;
        brand = Match(carPath) ?? Match(carName);
        if (Known.Count < 512) Known[key] = brand;
        return brand;
    }

    private static string? Match(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string padded = string.Concat(text.Trim().ToLowerInvariant(), " ");
        foreach (var (part, brand) in Rules)
            if (padded.Contains(part, StringComparison.Ordinal)) return brand;
        return null;
    }
}
