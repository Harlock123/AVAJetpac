namespace AVAJetpac.Engine;

/// <summary>
/// The computer Jetman, for the demonstration and the tests. It knows what the rocket needs next (its next section,
/// a fuel pod, or him aboard) and where that is, and each few moments tries every way of flying (left, right or
/// neither; thrusting or not), each followed by every way again, on a copy of the planet, and takes the one that ends
/// nearest what it's after without an alien getting it. The laser fires at any alien in line.
/// </summary>
public sealed class Pilot
{
    public const int First = 18, Then = 30, Every = 3;
    private (int Dx, bool Thrust) _last;
    private int _wait;
    private static readonly (int, bool)[] Moves = [(-1, false), (-1, true), (0, false), (0, true), (1, false), (1, true)];

    public string Doing { get; private set; } = "";

    public void Reset()
    {
        _last = (0, false);
        _wait = 0;
        Doing = "";
    }

    public Controls Decide(GameEngine g) => g.Phase != Phase.Play ? default : Decide(g.World);

    public Controls Decide(World w)
    {
        if (w.Man.Dead || w.Launching) return default;
        if (_wait-- <= 0)
        {
            Map(w);
            _wait = Every - 1;
            double best = double.NegativeInfinity;
            var pick = _last;
            foreach (var a in Moves)
            {
                double v = Value(w, a);
                if (a == _last) v += 3;
                if (v > best) { best = v; pick = a; }
            }
            _last = pick;
        }
        Doing = w.Carried != null ? "to the rocket" : w.Ready ? "aboard" : w.Built < 3 ? "after a section" : "after fuel";
        // Facing the way it's going, or toward an alien in line if standing.
        return new Controls(_last.Dx, _last.Thrust, Gun(w));
    }

    /// <summary>Where it's making for.</summary>
    public static (double X, double Y) Target(World w)
    {
        // Carrying something: anywhere over the rocket will do, at whatever height he's at.
        if (w.Carried != null) return (Field.RocketX, Math.Min(w.Man.Y, Field.Ground - w.Built * Field.SectionH - 20));
        if (w.Ready) return (Field.RocketX, Field.Ground);
        var want = w.Items.Where(i => !i.Dropping && (i.Kind == ItemKind.Section && i.Section == w.Built + 1 || i.Kind == ItemKind.Fuel && w.Built == 3))
            .OrderBy(i => Math.Abs(Sim.Across(w.Man.X, i.X))).FirstOrDefault();
        if (want != null) return (want.X, want.Y);
        var gem = w.Items.FirstOrDefault(i => i.Kind == ItemKind.Gem);
        if (gem != null) return (gem.X, gem.Y);
        return (Field.RocketX - 200, 560);
    }

    private double Value(World w, (int Dx, bool Thrust) a)
    {
        var c = w.Copy();
        for (int i = 0; i < First; i++)
        {
            Sim.Step(c, new Controls(a.Dx, a.Thrust, Gun(c)));
            if (c.Man.Dead) return -1e6 + i;
            if (Done(w, c)) return 1e5 - i;
        }
        double best = double.NegativeInfinity;
        foreach (var b in Moves)
        {
            var d = c.Copy();
            double v = 0;
            for (int i = 0; i < Then; i++)
            {
                Sim.Step(d, new Controls(b.Item1, b.Item2, Gun(d)));
                if (d.Man.Dead) { v = -1e6 + First + i; break; }
                if (Done(w, d)) { v = 1e5 - First - i; break; }
            }
            if (v == 0) v = Score(d) + (d.Score - w.Score) * 3;
            best = Math.Max(best, v);
        }
        return best;
    }

    /// <summary>Something got done: a pick-up, a section or fuel fitted, or off in the rocket.</summary>
    private static bool Done(World before, World after) =>
        after.Launching || after.Built > before.Built || after.Fuel > before.Fuel || (after.Carried != null) != (before.Carried != null);

    private double Score(World w)
    {
        var m = w.Man;
        double v = -Distance(m.X, m.Y);
        // Keep away from the aliens.
        foreach (var a in w.Aliens)
        {
            double ax = Sim.Across(m.X, a.X), ay = a.Y - (m.Y - 24), d = Math.Sqrt(ax * ax + ay * ay);
            if (d < 120) v -= (120 - d) * 2;
        }
        return v;
    }

    // ───────────────────────────── the way round the ledges ─────────────────────────────

    /// <summary>The map is in squares of this many dots.</summary>
    public const int Cell = 16;
    private const int Ci = (int)(Field.Width / Cell), Cj = (int)(Field.Height / Cell);
    private double[,]? _map;
    private (int, int) _mapFor = (-1, -1);

    /// <summary>How far Jetman (feet at x, y) is from where he's making for, going round the ledges.</summary>
    private double Distance(double x, double y)
    {
        if (_map == null) return 0;
        int i = ((int)(Sim.Wrap(x) / Cell) % Ci + Ci) % Ci, j = Math.Clamp((int)((y - Jetman.Height / 2) / Cell), 0, Cj - 1);
        double d = _map[i, j];
        if (d >= 1e8)
            for (int a = -1; a <= 1; a++)
                for (int b = -1; b <= 1; b++)
                    if (j + b >= 0 && j + b < Cj) d = Math.Min(d, _map[(i + a + Ci) % Ci, j + b] + 1);
        return d * Cell;
    }

    /// <summary>A map of how far every square is from the target, for a body Jetman's size.</summary>
    private void Map(World w)
    {
        var (tx, ty) = Target(w);
        var key = ((int)(tx / Cell), (int)((ty - Jetman.Height / 2) / Cell));
        if (_map != null && key == _mapFor) return;
        _mapFor = key;
        _map = new double[Ci, Cj];
        bool Fits(int i, int j)
        {
            double cx = (i + 0.5) * Cell, cy = (j + 0.5) * Cell;
            double y0 = cy - Jetman.Height / 2, y1 = cy + Jetman.Height / 2;
            if (y0 < Field.Top || y1 > Field.Ground) return false;
            foreach (var (l, r, ly) in Field.Ledges)
                if (cx + Jetman.HalfWidth > l && cx - Jetman.HalfWidth < r && y1 > ly && y0 < ly + Field.LedgeDepth) return false;
            return true;
        }
        for (int i = 0; i < Ci; i++)
            for (int j = 0; j < Cj; j++) _map[i, j] = 1e9;
        var q = new Queue<(int, int)>();
        int gi = (key.Item1 % Ci + Ci) % Ci, gj = Math.Clamp(key.Item2, 0, Cj - 1);
        for (int b = -2; b <= 2; b++)
            if (gj + b >= 0 && gj + b < Cj && Fits(gi, gj + b)) { _map[gi, gj + b] = 0; q.Enqueue((gi, gj + b)); }
        while (q.Count > 0)
        {
            var (i, j) = q.Dequeue();
            foreach (var (di, dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int a = (i + di + Ci) % Ci, b = j + dj;
                if (b < 0 || b >= Cj || _map[a, b] < 1e9 || !Fits(a, b)) continue;
                _map[a, b] = _map[i, j] + 1;
                q.Enqueue((a, b));
            }
        }
    }

    /// <summary>An alien in line, ahead of the gun.</summary>
    private static bool Gun(World w)
    {
        var m = w.Man;
        if (m.FirePrev) return false;
        foreach (var a in w.Aliens)
        {
            double along = Sim.Across(m.X, a.X) * m.Facing;
            if (along > 0 && along < 600 && Math.Abs(a.Y - (m.Y - 30)) < 22) return true;
        }
        return false;
    }
}
