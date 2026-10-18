namespace AVAJetpac.Engine;

/// <summary>The rules, one 60th of a second at a time.</summary>
public static class Sim
{
    public const int FuelPods = 6, PartPoints = 100, FuelPoints = 100, GemPoints = 250;
    public const double ItemFall = 150, LaunchSpeed = 220, ShotSpeed = 1500, ShotLength = 190, ShotLife = 0.45;

    public static int AlienPoints(AlienKind k) => 25 + 10 * (int)k;

    /// <summary>A level begins: every fourth one with a new rocket in pieces, the others with it standing empty.</summary>
    public static void StartLevel(World w, int level)
    {
        w.Level = level;
        w.Man = new Jetman();
        w.Aliens.Clear();
        w.Items.Clear();
        w.Shots.Clear();
        w.Fuel = 0;
        w.Launch = 0;
        w.Launching = w.Gone = false;
        w.Dice = new Dice(level * 101);
        w.NextAlien = 1.5;
        w.NextDrop = 1;
        if ((level - 1) % 4 == 0)
        {
            w.Built = 1;
            var a = Field.Ledges[0];
            var c = Field.Ledges[2];
            w.Items.Add(new Item { Kind = ItemKind.Section, Section = 2, X = (a.L + a.R) / 2, Y = a.Y, Landed = true });
            w.Items.Add(new Item { Kind = ItemKind.Section, Section = 3, X = (c.L + c.R) / 2, Y = c.Y, Landed = true });
        }
        else w.Built = 3;
    }

    /// <summary>After a life is lost: Jetman back by the rocket, whatever he carried dropped where he was, the aliens gone.</summary>
    public static void Respawn(World w)
    {
        if (w.Carried is { } c) { c.Carried = false; c.Landed = false; }
        w.Man = new Jetman();
        w.Aliens.Clear();
        w.Shots.Clear();
        w.NextAlien = 1.5;
    }

    /// <summary>Whether a box (left, top, right, bottom) overlaps a ledge.</summary>
    private static int LedgeHit(double x0, double y0, double x1, double y1)
    {
        for (int i = 0; i < Field.Ledges.Length; i++)
        {
            var (l, r, y) = Field.Ledges[i];
            if (x1 > l && x0 < r && y1 > y && y0 < y + Field.LedgeDepth) return i;
        }
        return -1;
    }

    /// <summary>The surface something falling at x lands on, below a height.</summary>
    public static double Surface(double x, double y)
    {
        double best = Field.Ground;
        foreach (var (l, r, ly) in Field.Ledges)
            if (x > l && x < r && ly >= y - 0.5 && ly < best) best = ly;
        return best;
    }

    public static double Wrap(double x) => (x % Field.Width + Field.Width) % Field.Width;

    /// <summary>The shortest way across from a to b, the screen wrapping round.</summary>
    public static double Across(double a, double b)
    {
        double d = b - a;
        if (d > Field.Width / 2) d -= Field.Width;
        if (d < -Field.Width / 2) d += Field.Width;
        return d;
    }

    public static void Step(World w, Controls c)
    {
        double dt = Field.Step;
        w.Clock += dt;
        for (int i = w.Pops.Count - 1; i >= 0; i--)
            if ((w.Pops[i].T += dt) > 0.4) w.Pops.RemoveAt(i);
        if (w.Launching)
        {
            w.Launch += LaunchSpeed * dt;
            if (w.Launch > Field.Ground) w.Gone = true;
            return;
        }
        var m = w.Man;
        if (!m.Dead) Move(w, m, c);
        Items(w);
        Aliens(w);
        Shots(w);
        Drops(w);
        if (!m.Dead && w.Ready && Math.Abs(Across(m.X, Field.RocketX)) < 30 && m.Y > Field.Ground - 3 * Field.SectionH)
        {
            w.Launching = true;
            w.Heard.Add(Sfx.Launch);
        }
    }

    private static void Move(World w, Jetman m, Controls c)
    {
        double dt = Field.Step;
        if (c.Dx != 0) m.Facing = c.Dx;
        double vx = c.Dx * (m.Grounded ? Jetman.Walk : Jetman.Fly);
        if (m.Grounded && c.Dx != 0) m.Step += dt * 8;
        // Across, wrapping round; a ledge's end stops him.
        double nx = Wrap(m.X + vx * dt);
        if (LedgeHit(nx - Jetman.HalfWidth, m.Y - Jetman.Height, nx + Jetman.HalfWidth, m.Y - 1) < 0) m.X = nx;
        // Up and down.
        m.Vy = Math.Clamp(m.Vy + (Jetman.Gravity - (c.Thrust ? Jetman.Lift : 0)) * dt, -Jetman.MaxRise, Jetman.MaxFall);
        double ny = m.Y + m.Vy * dt;
        m.Grounded = false;
        int hit = LedgeHit(m.X - Jetman.HalfWidth, ny - Jetman.Height, m.X + Jetman.HalfWidth, ny);
        if (hit >= 0)
        {
            var (_, _, ly) = Field.Ledges[hit];
            if (m.Vy > 0 && m.Y <= ly + 0.5) { ny = ly; m.Grounded = true; }
            else if (m.Vy < 0) ny = ly + Field.LedgeDepth + Jetman.Height;
            m.Vy = 0;
        }
        if (ny >= Field.Ground) { ny = Field.Ground; m.Vy = Math.Min(0, m.Vy); m.Grounded = true; }
        if (ny - Jetman.Height < Field.Top) { ny = Field.Top + Jetman.Height; m.Vy = 0; }
        m.Y = ny;
        bool fire = c.Fire && !m.FirePrev;
        m.FirePrev = c.Fire;
        if (fire && w.Shots.Count < 2)
        {
            w.Shots.Add(new Shot(m.X + m.Facing * 22, m.Y - 30, m.Facing));
            w.Heard.Add(Sfx.Laser);
        }
        // Picking things up.
        if (w.Carried == null)
            foreach (var it in w.Items)
            {
                if (it.Dropping || Math.Abs(Across(m.X, it.X)) > 30 || it.Y < m.Y - Jetman.Height - 10 || it.Y > m.Y + 30) continue;
                if (it.Kind == ItemKind.Gem)
                {
                    w.Items.Remove(it);
                    w.Score += GemPoints;
                    w.Heard.Add(Sfx.Gem);
                    break;
                }
                if (it.Kind == ItemKind.Section && it.Section != w.Built + 1 || it.Kind == ItemKind.Fuel && w.Built < 3) continue;
                it.Carried = true;
                it.Landed = false;
                w.Heard.Add(Sfx.Pick);
                break;
            }
    }

    /// <summary>What's being carried rides under Jetman, and drops onto the rocket once he's over it.</summary>
    private static void Items(World w)
    {
        var m = w.Man;
        foreach (var it in w.Items)
        {
            if (it.Carried)
            {
                // Carried hanging below him (but never into the ground).
                it.X = m.X;
                it.Y = Math.Min(Field.Ground, m.Y + (it.Kind == ItemKind.Section ? Field.SectionH : 34));
                if (Math.Abs(Across(m.X, Field.RocketX)) < 14)
                {
                    it.Carried = false;
                    it.Dropping = true;
                    it.X = Field.RocketX;
                    w.Heard.Add(Sfx.Drop);
                }
                continue;
            }
            if (it.Landed) continue;
            it.Y += ItemFall * Field.Step;
            double top = it.Dropping ? Field.Ground - w.Built * Field.SectionH : Surface(it.X, it.Y - ItemFall * Field.Step);
            if (it.Y >= top)
            {
                it.Y = top;
                if (it.Dropping) Fit(w, it);
                else it.Landed = true;
            }
        }
        w.Items.RemoveAll(i => i.Kind != ItemKind.Gem && i.Dropping && i.Landed);
    }

    /// <summary>A section or a fuel pod lands on the rocket and becomes part of it.</summary>
    private static void Fit(World w, Item it)
    {
        it.Landed = true;
        if (it.Kind == ItemKind.Section) { w.Built++; w.Score += PartPoints; }
        else if (it.Kind == ItemKind.Fuel) { w.Fuel++; w.Score += FuelPoints; }
        w.Heard.Add(w.Ready ? Sfx.Ready : Sfx.Fit);
    }

    /// <summary>Fuel pods fall from the sky one at a time once the rocket's built; now and then a gem.</summary>
    private static void Drops(World w)
    {
        w.NextDrop -= Field.Step;
        if (w.NextDrop > 0) return;
        w.NextDrop = 2;
        double x = 80 + w.Dice.Next() * (Field.Width - 160);
        if (Math.Abs(x - Field.RocketX) < 80) x = Wrap(x + 200);
        if (w.Built == 3 && w.Fuel < FuelPods && !w.Items.Any(i => i.Kind == ItemKind.Fuel))
            w.Items.Add(new Item { Kind = ItemKind.Fuel, X = x, Y = Field.Top });
        else if (!w.Items.Any(i => i.Kind == ItemKind.Gem) && w.Dice.Next() < 0.25)
            w.Items.Add(new Item { Kind = ItemKind.Gem, X = x, Y = Field.Top });
    }

    private static void Aliens(World w)
    {
        double dt = Field.Step;
        var m = w.Man;
        int most = Math.Min(7, 3 + w.Level / 3);
        w.NextAlien -= dt;
        if (w.NextAlien <= 0 && w.Aliens.Count < most && !m.Dead)
        {
            w.NextAlien = Math.Max(0.45, 1.3 - w.Level * 0.04);
            bool left = w.Dice.Next(2) == 0;
            double speed = 110 + Math.Min(w.Level, 16) * 7;
            var a = new Alien { Kind = w.Kind, X = left ? 0 : Field.Width, Y = Field.Top + 40 + w.Dice.Next() * (Field.Ground - Field.Top - 120), Speed = speed };
            a.Vx = (left ? 1 : -1) * speed * (0.8 + w.Dice.Next() * 0.4);
            a.Vy = w.Kind == AlienKind.Meteor ? speed * (0.3 + w.Dice.Next() * 0.4) : (w.Dice.Next() - 0.5) * speed;
            w.Aliens.Add(a);
        }
        for (int i = w.Aliens.Count - 1; i >= 0; i--)
        {
            var a = w.Aliens[i];
            a.T += dt;
            bool homes = a.Kind is AlienKind.Jet or AlienKind.Saucer or AlienKind.Falcon;
            if (homes && !m.Dead)
            {
                double dx = Across(a.X, m.X), dy = m.Y - 25 - a.Y, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
                double turn = 1.1 * dt;
                a.Vx += (dx / d * a.Speed - a.Vx) * turn;
                a.Vy += (dy / d * a.Speed - a.Vy) * turn;
                // Always at full speed, whatever it's bounced off.
                double v = Math.Max(1, Math.Sqrt(a.Vx * a.Vx + a.Vy * a.Vy));
                a.Vx *= a.Speed / v;
                a.Vy *= a.Speed / v;
            }
            double nx = Wrap(a.X + a.Vx * dt), ny = a.Y + a.Vy * dt;
            bool hits = LedgeHit(nx - 14, ny - 12, nx + 14, ny + 12) >= 0 || ny + 12 > Field.Ground || ny - 12 < Field.Top;
            if (hits)
            {
                if (a.Kind == AlienKind.Meteor) { Burst(w, i, score: false); continue; }
                // Bounce: back the way it came on whichever axis hit.
                if (LedgeHit(nx - 14, a.Y - 12, nx + 14, a.Y + 12) >= 0) a.Vx = -a.Vx;
                else a.Vy = -a.Vy;
                nx = Wrap(a.X + a.Vx * dt);
                ny = a.Y + a.Vy * dt;
            }
            a.X = nx;
            a.Y = Math.Clamp(ny, Field.Top + 12, Field.Ground - 12);
            if (!m.Dead && Math.Abs(Across(a.X, m.X)) < Jetman.HalfWidth + 12 && a.Y > m.Y - Jetman.Height - 8 && a.Y < m.Y + 8)
            {
                Burst(w, i, score: false);
                m.Dead = true;
                w.Cause = a.Kind.ToString().ToLowerInvariant();
                w.Heard.Add(Sfx.Die);
            }
        }
    }

    private static void Burst(World w, int i, bool score)
    {
        var a = w.Aliens[i];
        w.Pops.Add(new Pop(a.X, a.Y));
        w.Aliens.RemoveAt(i);
        if (score) w.Score += AlienPoints(a.Kind);
        w.Heard.Add(Sfx.Pop);
    }

    /// <summary>The laser: a streak that shoots out ahead and fades.</summary>
    private static void Shots(World w)
    {
        for (int s = w.Shots.Count - 1; s >= 0; s--)
        {
            var sh = w.Shots[s];
            sh.T += Field.Step;
            sh.Head = Math.Min(sh.T * ShotSpeed, 700);
            if (sh.T > ShotLife) { w.Shots.RemoveAt(s); continue; }
            double tail = Math.Max(0, sh.Head - ShotLength);
            for (int i = w.Aliens.Count - 1; i >= 0; i--)
            {
                var a = w.Aliens[i];
                double along = Across(sh.X, a.X) * sh.Dir;
                if (along >= tail - 14 && along <= sh.Head + 14 && Math.Abs(a.Y - sh.Y) < 18)
                {
                    Burst(w, i, score: true);
                    w.Shots.RemoveAt(s);
                    break;
                }
            }
        }
    }
}
