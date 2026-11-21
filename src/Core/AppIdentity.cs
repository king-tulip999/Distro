namespace Core;

public enum AppKind { Tecnica, Velocita }

public static class AppIdentity
{
    public static AppKind Kind { get; private set; } = AppKind.Tecnica;

    public static bool SimHub { get; private set; }

    public static void Use(AppKind kind) => Kind = kind;

    public static void UseSimHub()
    {
        Kind = AppKind.Velocita;
        SimHub = true;
    }

    public static string Name => Kind == AppKind.Tecnica ? "Tecnica" : "Velocita";

    public static string DataName => SimHub ? "Velocita SimHub" : Name;

    public static string ExeName => Name + ".exe";

    public const string SimHubExe = "VelocitaSimHub.exe";

    public static bool IsTecnica => Kind == AppKind.Tecnica;

    public static bool NeedsAdmin => IsTecnica;

    public const string OldDataFolder = "Pit-Box";
}
