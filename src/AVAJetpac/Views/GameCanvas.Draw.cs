using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAJetpac.Engine;

namespace AVAJetpac.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAJetpac/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Cyan = 0xFF6AE8FF, Orange = 0xFFFF9A30;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);


    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused;

    private static readonly uint[] AlienColours = [0xFFFF6040, 0xFF40E0FF, 0xFFE040FF, 0xFFFFE040, 0xFF40FF80, 0xFFFF40A0, 0xFFFFA020, 0xFF80A0FF];

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawPlanet(dc, null); DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawPlanet(dc, null); DrawInitials(dc); break;
            case GameState.GameOver: DrawGameOver(dc); break;
            default:
                DrawPlanet(dc, g.World);
                DrawTop(dc, g.World);
                if (g.Message is { } msg && g.Phase is Phase.Start)
                {
                    dc.FillRectangle(Brush(0xB0000000), new Rect(0, 330, Field.Width, 70));
                    Text(dc, msg, new Point(512, 365), 52, Yellow, center: true);
                }
                break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0xD0000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(512, 300), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 360), 26, White, center: true);
        }
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, Field.Height - 18), 22, Yellow, center: true);
    }

    /// <summary>Space, the ledges and the ground, and (in play) the rocket and everyone about.</summary>
    private void DrawPlanet(DrawingContext dc, World? w)
    {
        dc.FillRectangle(Brush(0xFF000008), new Rect(0, 0, Field.Width, Field.Height));
        for (int k = 0; k < 90; k++)
        {
            double tw = 0.5 + 0.5 * Math.Sin(_clock * (1 + k % 4) + k);
            dc.FillEllipse(Hash(k, 1, 2) % 1024, Field.Top + Hash(k, 2, 2) % 640, 0.8 + Hash(k, 3, 2) % 2 * 0.6, Alpha(0xFFE0E0FF, 0.3 + 0.6 * tw));
        }
        // A far planet.
        dc.FillEllipse(140, 180, 50, 0xFF303060);
        dc.DrawEllipse(null, Pen(0xFF6060A0, 3), new Point(140, 180), 80, 14);
        foreach (var (l, r, y) in Field.Ledges) DrawLedge(dc, l, r, y);
        dc.FillRectangle(Brush(0xFFE0C030), new Rect(0, Field.Ground, Field.Width, 8));
        dc.FillRectangle(Brush(0xFF806010), new Rect(0, Field.Ground + 8, Field.Width, Field.Height - Field.Ground - 8));
        if (w == null) return;
        DrawRocket(dc, w);
        foreach (var it in w.Items) DrawItem(dc, it, w);
        foreach (var a in w.Aliens) DrawAlien(dc, a, w);
        foreach (var p in w.Pops)
            for (int k = 0; k < 8; k++)
            {
                double ang = k * Math.PI / 4, r = 8 + p.T * 90;
                dc.FillEllipse(p.X + Math.Cos(ang) * r, p.Y + Math.Sin(ang) * r, 4 * (1 - p.T / 0.4), Alpha(AlienColours[(k + (int)(p.T * 30)) % 8], 1 - p.T / 0.4));
            }
        foreach (var s in w.Shots) DrawShot(dc, s);
        if (!w.Launching && !(w.Man.Dead && (int)(Engine.PhaseTime * 10) % 2 == 0)) DrawJetman(dc, w.Man);
    }

    private static void DrawLedge(DrawingContext dc, double l, double r, double y)
    {
        dc.FillRectangle(Brush(0xFF30C030), new Rect(l, y, r - l, Field.LedgeDepth));
        for (double x = l; x < r; x += 12) dc.FillRectangle(Brush(0xFF20802A), new Rect(x, y + 6, 6, 12));
        dc.FillRectangle(Brush(0xFF80FF80), new Rect(l, y, r - l, 3));
    }

    private void DrawRocket(DrawingContext dc, World w)
    {
        double x = Field.RocketX, h = Field.SectionH, rise = w.Launch;
        int filled = w.Fuel;
        for (int s = 1; s <= w.Built; s++)
        {
            double bottom = Field.Ground - (s - 1) * h - rise;
            // Each section fills with fuel, two pods to a section.
            uint body = filled >= s * 2 ? 0xFFE040E0 : filled >= s * 2 - 1 ? 0xFFB0A0D0 : 0xFFE0E0F0;
            DrawSection(dc, x, bottom, s, body);
        }
        if (w.Launching)
        {
            for (int k = 0; k < 6; k++)
            {
                double fy = Field.Ground - rise + 8 + k * 14 + Math.Sin(_clock * 30 + k) * 4;
                dc.FillEllipse(x + Math.Sin(_clock * 20 + k) * 8, fy, 18 - k * 2, Alpha(k < 2 ? 0xFFFFF0A0 : 0xFFFF6020, 1 - k / 7.0));
            }
        }
        if (w.Ready && !w.Launching && (int)(_clock * 4) % 2 == 0)
            Text(dc, "ALL ABOARD!", new Point(x, Field.Ground - 3 * h - 30), 24, Yellow, center: true);
    }

    private static void DrawSection(DrawingContext dc, double x, double bottom, int s, uint body)
    {
        double h = Field.SectionH;
        if (s == 3)
        {
            // The nose.
            Poly(dc, body, 0xFF606080, 2, new Point(x - 24, bottom), new Point(x + 24, bottom), new Point(x + 12, bottom - h * 0.6), new Point(x, bottom - h), new Point(x - 12, bottom - h * 0.6));
            dc.FillEllipse(x, bottom - h * 0.45, 7, 0xFF40A0E0);
            return;
        }
        dc.FillRectangle(Brush(body), new Rect(x - 24, bottom - h, 48, h));
        dc.DrawRectangle(null, Pen(0xFF606080, 2), new Rect(x - 24, bottom - h, 48, h));
        dc.DrawLine(Pen(0xFF606080, 2), new Point(x - 24, bottom - h / 2), new Point(x + 24, bottom - h / 2));
        if (s == 1)
        {
            // The fins.
            Poly(dc, 0xFFE03030, null, 0, new Point(x - 24, bottom - h * 0.6), new Point(x - 40, bottom), new Point(x - 24, bottom));
            Poly(dc, 0xFFE03030, null, 0, new Point(x + 24, bottom - h * 0.6), new Point(x + 40, bottom), new Point(x + 24, bottom));
        }
    }

    private void DrawItem(DrawingContext dc, Item it, World w)
    {
        switch (it.Kind)
        {
            case ItemKind.Section:
                DrawSection(dc, it.X, it.Y, it.Section, 0xFFE0E0F0);
                break;
            case ItemKind.Fuel:
                dc.FillRectangle(Brush(0xFFC040E0), new Rect(it.X - 18, it.Y - 30, 36, 30), 6);
                dc.FillRectangle(Brush(0xFFE0A0FF), new Rect(it.X - 12, it.Y - 36, 24, 7), 3);
                Text(dc, "FUEL", new Point(it.X, it.Y - 15), 14, 0xFFFFFFFF, center: true, shadow: false);
                break;
            case ItemKind.Gem:
            {
                uint c = AlienColours[(int)(_clock * 6) % 8];
                Poly(dc, c, 0xFFFFFFFF, 1.5, new Point(it.X - 14, it.Y - 14), new Point(it.X - 8, it.Y - 22), new Point(it.X + 8, it.Y - 22), new Point(it.X + 14, it.Y - 14), new Point(it.X, it.Y));
                break;
            }
        }
    }

    private void DrawAlien(DrawingContext dc, Alien a, World w)
    {
        uint c = AlienColours[(int)a.Kind];
        double x = a.X, y = a.Y, f = a.Vx >= 0 ? 1 : -1;
        switch (a.Kind)
        {
            case AlienKind.Meteor:
                for (int k = 1; k < 5; k++) dc.FillEllipse(x - a.Vx * 0.03 * k, y - a.Vy * 0.03 * k, 10 - k * 2, Alpha(0xFFFFA020, 0.6 - k * 0.12));
                dc.FillEllipse(x, y, 12, c);
                dc.FillEllipse(x - 3, y - 3, 4, 0xFFFFE0C0);
                break;
            case AlienKind.Fuzz or AlienKind.Bubble:
                for (int k = 0; k < 10; k++)
                {
                    double ang = k * Math.PI / 5 + _clock * 3;
                    dc.DrawLine(Pen(c, 2), new Point(x, y), new Point(x + Math.Cos(ang) * 15, y + Math.Sin(ang) * 15));
                }
                dc.FillEllipse(x, y, a.Kind == AlienKind.Bubble ? 12 : 9, a.Kind == AlienKind.Bubble ? Alpha(c, 0.6) : c);
                dc.FillEllipse(x - 4, y - 3, 2.5, 0xFF000000);
                dc.FillEllipse(x + 4, y - 3, 2.5, 0xFF000000);
                break;
            case AlienKind.Jet or AlienKind.Falcon:
                Poly(dc, c, 0xFFFFFFFF, 1.5, new Point(x + 18 * f, y), new Point(x - 14 * f, y - 12), new Point(x - 8 * f, y), new Point(x - 14 * f, y + 12));
                break;
            case AlienKind.Saucer:
                dc.DrawEllipse(Brush(c), Pen(0xFFFFFFFF, 1.5), new Point(x, y + 2), 18, 7);
                dc.DrawEllipse(Brush(0xA0A0E0FF), null, new Point(x, y - 4), 9, 7);
                break;
            case AlienKind.Cross:
                dc.DrawLine(Pen(c, 6), new Point(x - 13, y - 13), new Point(x + 13, y + 13));
                dc.DrawLine(Pen(c, 6), new Point(x + 13, y - 13), new Point(x - 13, y + 13));
                break;
            default:
                dc.DrawEllipse(Brush(c), Pen(0xFF205020, 1.5), new Point(x, y), 15, 11);
                dc.FillEllipse(x - 6, y - 9, 4, 0xFFFFFFFF);
                dc.FillEllipse(x + 6, y - 9, 4, 0xFFFFFFFF);
                break;
        }
    }

    private static void DrawShot(DrawingContext dc, Shot s)
    {
        double tail = Math.Max(0, s.Head - Sim.ShotLength);
        for (double d = tail; d < s.Head; d += 16)
        {
            double x = Sim.Wrap(s.X + s.Dir * d);
            dc.DrawLine(Pen(AlienColours[(int)(d / 16) % 8], 4), new Point(x, s.Y), new Point(x + s.Dir * 10, s.Y));
        }
    }

    private void DrawJetman(DrawingContext dc, Jetman m)
    {
        double x = m.X, y = m.Y;
        int f = m.Facing;
        // The jetpack, and its flame when airborne.
        dc.FillRectangle(Brush(0xFF909090), new Rect(x - f * 14 - 6, y - 40, 12, 22), 3);
        if (!m.Grounded)
        {
            double fl = 10 + Math.Sin(_clock * 40) * 4;
            Poly(dc, 0xFFFF8020, null, 0, new Point(x - f * 14 - 5, y - 18), new Point(x - f * 14, y - 18 + fl), new Point(x - f * 14 + 5, y - 18));
        }
        double step = m.Grounded ? Math.Sin(m.Step * 2) * 6 : 3;
        dc.DrawLine(Pen(0xFFF0F0F0, 7), new Point(x - 3, y - 18), new Point(x - 3 - step, y - 1));
        dc.DrawLine(Pen(0xFFF0F0F0, 7), new Point(x + 3, y - 18), new Point(x + 3 + step, y - 1));
        dc.FillRectangle(Brush(0xFFF0F0F0), new Rect(x - 10, y - 38, 20, 22), 5);
        dc.DrawLine(Pen(0xFFF0F0F0, 6), new Point(x, y - 32), new Point(x + f * 18, y - 30));
        dc.FillRectangle(Brush(0xFF707070), new Rect(x + f * 14 - 4, y - 34, 10, 6));
        dc.FillEllipse(x, y - 46, 10, 0xFFF0F0F0);
        dc.FillRectangle(Brush(0xFF40A0E0), new Rect(x + f * 1 - 7 + f * 3, y - 50, 12, 7), 3);
    }

    private void DrawTop(DrawingContext dc, World w)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF000000), new Rect(0, 0, Field.Width, Field.Top));
        Text(dc, "1UP", new Point(60, 18), 24, Yellow, center: true);
        Text(dc, $"{g.Score:000000}", new Point(60, 44), 30, White, center: true);
        Text(dc, "HI", new Point(512, 18), 24, Yellow, center: true);
        Text(dc, $"{Math.Max(g.HighScores.TopScore, g.Score):000000}", new Point(512, 44), 30, White, center: true);
        Text(dc, $"LEVEL {w.Level}", new Point(300, 32), 28, Cyan, center: true);
        Text(dc, $"FUEL {w.Fuel}/{Sim.FuelPods}", new Point(740, 32), 28, 0xFFE040E0, center: true);
        for (int i = 0; i < Math.Min(g.Lives - 1, 6); i++)
        {
            double x = 1000 - i * 22;
            dc.FillRectangle(Brush(0xFFF0F0F0), new Rect(x - 6, 22, 12, 18), 3);
            dc.FillEllipse(x, 18, 6, 0xFFF0F0F0);
        }
    }

    // ───────────────────────────── title, game over and scores ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        DrawPlanet(dc, null);
        var w = new World { Built = 3, Fuel = (int)(_clock * 2) % 7 };
        DrawRocket(dc, w);
        DrawJetman(dc, new Jetman { X = 500 + Math.Sin(_clock) * 200, Y = 600 + Math.Sin(_clock * 1.7) * 60, Grounded = false, Facing = Math.Cos(_clock) > 0 ? 1 : -1 });
        dc.FillRectangle(Brush(0xC0000000), new Rect(cx - 410, 70, 820, 300), 12);
        string title = "AVA JETPAC";
        for (int i = 0; i < title.Length; i++)
        {
            double tx = cx - title.Length * 30 + 30 + i * 60, ty = 128 + Math.Sin(_clock * 2.4 + i * 0.45) * 5;
            Text(dc, title[i].ToString(), new Point(tx, ty), 112, AlienColours[i % 8], center: true);
        }
        Text(dc, "AFTER ULTIMATE PLAY THE GAME'S 1983 GAME", new Point(cx, 196), 26, White, center: true);
        Text(dc, "BUILD YOUR ROCKET: CARRY ITS SECTIONS TO THE PAD, THEN FUEL IT", new Point(cx, 244), 24, White, center: true);
        Text(dc, "POD BY POD AS THEY FALL. CLIMB ABOARD AND BLAST OFF TO THE NEXT", new Point(cx, 272), 24, White, center: true);
        Text(dc, "PLANET. LASER THE ALIENS - A NEW KIND ON EVERY PLANET.", new Point(cx, 300), 24, White, center: true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER TO START", new Point(cx, 344), 40, Yellow, center: true);
        dc.FillRectangle(Brush(0xD0000000), new Rect(0, 722, Field.Width, 46));
        Text(dc, "LEFT/RIGHT: MOVE   UP OR SHIFT: THRUST   SPACE: FIRE   P: PAUSE   M: SOUND   MUSIC: A TUNE OF ITS OWN", new Point(cx, 745), 19, 0xFFE0E0C0, center: true);
    }

    private void DrawGameOver(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        DrawPlanet(dc, null);
        dc.FillRectangle(Brush(0xC0000000), new Rect(cx - 360, 150, 720, 230), 12);
        Text(dc, "GAME OVER", new Point(cx, 220), 100, (int)(_clock * 4) % 2 == 0 ? Red : Yellow, center: true);
        Text(dc, $"SCORE {g.Score}", new Point(cx, 300), 46, White, center: true);
        Text(dc, $"YOU REACHED LEVEL {g.World.Level}", new Point(cx, 350), 30, Cyan, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD0000000), new Rect(cx - 330, 70, 660, 640), 12);
        Text(dc, "THE BEST JETMEN", new Point(cx, 120), 60, Yellow, center: true);
        Text(dc, "RANK NAME    SCORE   LEVEL", new Point(cx, 190), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 236 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 680), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD0000000), new Rect(cx - 330, 140, 660, 460), 12);
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}

internal static class DrawingExtensions
{
    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
    public static void FillRectangle(this DrawingContext dc, IBrush brush, Rect rect, double radius) => dc.FillRectangle(brush, rect, (float)radius);
}
