namespace AVAJetpac.Engine;

/// <summary>The screen: the score line along the top, then the planet's surface with its three ledges.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const double Top = 64, Ground = 712;
    /// <summary>The ledges: left, right, and the height of their tops; each is 24 deep.</summary>
    public static readonly (double L, double R, double Y)[] Ledges = [(110, 330, 310), (450, 610, 440), (740, 960, 220)];
    public const double LedgeDepth = 24;
    /// <summary>Where the rocket stands.</summary>
    public const double RocketX = 690;
    public const double SectionH = 56;
}

/// <summary>A small random number generator whose state can be copied.</summary>
public struct Dice
{
    private ulong _s;
    public Dice(int seed) => _s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;

    public double Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 7;
        _s ^= _s << 17;
        return (_s >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int n) => (int)(Next() * n);
}

/// <summary>The controls: left or right, thrust (up), and fire.</summary>
public readonly record struct Controls(int Dx = 0, bool Thrust = false, bool Fire = false);

/// <summary>Jetman: where his feet are (x his middle), how he's moving, which way he faces.</summary>
public sealed class Jetman
{
    public const double HalfWidth = 14, Height = 48, Walk = 150, Fly = 230, Gravity = 650, Lift = 1350, MaxRise = 270, MaxFall = 300;
    public double X = Field.RocketX - 120, Y = Field.Ground, Vy, Step;
    public int Facing = 1;
    public bool Grounded = true, Dead, FirePrev;
    public Jetman Copy() => (Jetman)MemberwiseClone();
}

/// <summary>The kinds of alien, a new one each level: some fly straight and burst on whatever they hit, some bounce, some come after you.</summary>
public enum AlienKind { Meteor, Fuzz, Bubble, Jet, Saucer, Cross, Falcon, Frog }

public sealed class Alien
{
    public AlienKind Kind;
    public double X, Y, Vx, Vy, T, Speed;
    public Alien Copy() => (Alien)MemberwiseClone();
}

/// <summary>Something to pick up: a rocket section, a fuel pod, or a gem.</summary>
public enum ItemKind { Section, Fuel, Gem }

public sealed class Item
{
    public ItemKind Kind;
    public int Section;
    public double X, Y, Vy;
    public bool Landed, Carried, Dropping;
    public Item Copy() => (Item)MemberwiseClone();
}

public sealed record Shot(double X, double Y, int Dir)
{
    public double T, Head;
}

public sealed record Pop(double X, double Y)
{
    public double T;
}

/// <summary>
/// Everything on the planet that moves or changes, kept together so it can be copied whole: Jetman, the aliens, the
/// rocket (how much of it is built, how much fuel is in it), what's lying about to be picked up, the score.
/// </summary>
public sealed class World
{
    public int Level = 1;
    public Jetman Man = new();
    public List<Alien> Aliens = [];
    public List<Item> Items = [];
    public List<Shot> Shots = [];
    public List<Pop> Pops = [];
    /// <summary>How many of the rocket's three sections stand on the pad, and how many fuel pods are in it (six fill it).</summary>
    public int Built = 1, Fuel;
    /// <summary>Taking off: how far up it is (0 on the pad).</summary>
    public double Launch;
    public bool Launching, Gone;
    public int Score;
    public double Clock, NextAlien, NextDrop;
    public Dice Dice = new(1);
    public string Cause = "";
    public List<Sfx> Heard = [];

    public World Copy() => new()
    {
        Level = Level, Man = Man.Copy(), Aliens = Aliens.Select(a => a.Copy()).ToList(), Items = Items.Select(i => i.Copy()).ToList(),
        Shots = [.. Shots.Select(s => s with { })], Built = Built, Fuel = Fuel, Launch = Launch, Launching = Launching, Gone = Gone, Score = Score,
        Clock = Clock, NextAlien = NextAlien, NextDrop = NextDrop, Dice = Dice, Cause = Cause,
    };

    public AlienKind Kind => (AlienKind)((Level - 1) % 8);
    public bool Ready => Built == 3 && Fuel >= Sim.FuelPods;
    public Item? Carried => Items.FirstOrDefault(i => i.Carried);
}
