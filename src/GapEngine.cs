// GapEngine.cs - 2D gap / overshoot analysis for lines, arcs, circles and polylines.
//
// No AutoCAD dependency: this file compiles on its own (see tests\GapEngineTests.cs)
// and is shared unchanged by the AutoCAD plugin (GapCommands.cs).
//
// Written in C# 5 so that it builds with the csc.exe shipped in .NET Framework 4.x.

using System;
using System.Collections.Generic;

namespace LineGapChecker
{
    /// <summary>A 2D line segment in drawing units.</summary>
    public struct Seg2
    {
        public double X1, Y1, X2, Y2;

        public Seg2(double x1, double y1, double x2, double y2)
        {
            X1 = x1; Y1 = y1; X2 = x2; Y2 = y2;
        }
    }

    /// <summary>
    /// One drawing object (LINE, ARC, CIRCLE or polyline) as a chain of straight and circular
    /// pieces in drawing units. Pieces are kept in drawing order, so each piece starts where
    /// the previous one ends.
    /// </summary>
    public sealed class GapCurve
    {
        internal struct Piece
        {
            public bool IsArc;
            /// <summary>Start and end point, in drawing order.</summary>
            public double X1, Y1, X2, Y2;
            /// <summary>Arcs only: centre, radius, start angle and signed sweep (+ = counter-clockwise).</summary>
            public double Cx, Cy, R, A0, Sweep;
        }

        internal readonly Piece[] Pieces;
        /// <summary>A closed curve (circle, closed polyline) has no free ends.</summary>
        public readonly bool Closed;

        private GapCurve(Piece[] pieces, bool closed)
        {
            Pieces = pieces;
            Closed = closed;
        }

        public int PieceCount
        {
            get { return Pieces.Length; }
        }

        public static GapCurve FromLine(double x1, double y1, double x2, double y2)
        {
            return new GapCurve(new Piece[] { Straight(x1, y1, x2, y2) }, false);
        }

        /// <summary>
        /// An arc from <paramref name="startAngle"/> (radians) through a signed
        /// <paramref name="sweep"/>: positive runs counter-clockwise, negative clockwise.
        /// </summary>
        public static GapCurve FromArc(double cx, double cy, double radius, double startAngle, double sweep)
        {
            CheckRadius(radius);
            if (!(Math.Abs(sweep) <= GapEngine.TwoPi * (1 + 1e-12)))
                throw new ArgumentOutOfRangeException("sweep", "The sweep must be between -2*pi and 2*pi.");
            Piece p = MakeArc(cx, cy, radius, startAngle, sweep);
            p.X1 = cx + radius * Math.Cos(startAngle);
            p.Y1 = cy + radius * Math.Sin(startAngle);
            p.X2 = cx + radius * Math.Cos(startAngle + sweep);
            p.Y2 = cy + radius * Math.Sin(startAngle + sweep);
            return new GapCurve(new Piece[] { p }, false);
        }

        public static GapCurve FromCircle(double cx, double cy, double radius)
        {
            CheckRadius(radius);
            Piece p = MakeArc(cx, cy, radius, 0.0, GapEngine.TwoPi);
            p.X1 = p.X2 = cx + radius;
            p.Y1 = p.Y2 = cy;
            return new GapCurve(new Piece[] { p }, true);
        }

        /// <summary>
        /// A polyline through the given vertices. <paramref name="bulge"/> (may be null) holds the
        /// AutoCAD bulge of the segment that starts at each vertex: tan(sweep / 4), positive for a
        /// counter-clockwise arc. A closed polyline also joins the last vertex back to the first.
        /// </summary>
        public static GapCurve FromPolyline(double[] x, double[] y, double[] bulge, bool closed)
        {
            if (x == null) throw new ArgumentNullException("x");
            if (y == null) throw new ArgumentNullException("y");
            int n = x.Length;
            if (n == 0) throw new ArgumentException("A polyline needs at least one vertex.", "x");
            if (y.Length != n || (bulge != null && bulge.Length != n))
                throw new ArgumentException("x, y and bulge must have the same length.");

            int count = closed && n > 1 ? n : n - 1;
            if (count == 0)
                return new GapCurve(new Piece[] { Straight(x[0], y[0], x[0], y[0]) }, false);

            Piece[] pieces = new Piece[count];
            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % n;
                pieces[i] = Span(x[i], y[i], x[j], y[j], bulge == null ? 0.0 : bulge[i]);
            }
            return new GapCurve(pieces, closed && n > 1);
        }

        private static Piece Straight(double x1, double y1, double x2, double y2)
        {
            Piece p = new Piece();
            p.X1 = x1; p.Y1 = y1; p.X2 = x2; p.Y2 = y2;
            return p;
        }

        private static Piece MakeArc(double cx, double cy, double r, double a0, double sweep)
        {
            Piece p = new Piece();
            p.IsArc = true;
            p.Cx = cx; p.Cy = cy; p.R = r; p.A0 = a0; p.Sweep = sweep;
            return p;
        }

        /// <summary>One polyline segment from a bulge value. The end points are kept exactly.</summary>
        private static Piece Span(double x1, double y1, double x2, double y2, double b)
        {
            double vx = x2 - x1, vy = y2 - y1;
            if (Math.Abs(b) < 1e-10 || (vx == 0 && vy == 0) || double.IsNaN(b) || double.IsInfinity(b))
                return Straight(x1, y1, x2, y2);

            // The centre lies on the chord's perpendicular bisector: midpoint + k * left normal.
            double k = (1 - b * b) / (4 * b);
            double cx = (x1 + x2) * 0.5 - k * vy;
            double cy = (y1 + y2) * 0.5 + k * vx;
            double dx = x1 - cx, dy = y1 - cy;
            Piece p = MakeArc(cx, cy, Math.Sqrt(dx * dx + dy * dy), Math.Atan2(dy, dx), 4 * Math.Atan(b));
            p.X1 = x1; p.Y1 = y1; p.X2 = x2; p.Y2 = y2;
            return p;
        }

        private static void CheckRadius(double radius)
        {
            if (!(radius >= 0) || double.IsInfinity(radius))
                throw new ArgumentOutOfRangeException("radius", "The radius must be a finite value >= 0.");
        }
    }

    /// <summary>Analysis thresholds, all in drawing units.</summary>
    public sealed class GapOptions
    {
        /// <summary>Objects this long or shorter are "tiny"; overshoot stubs up to this length are trimmed.</summary>
        public double ShortLength;
        /// <summary>Largest gap that is reported as "needs extending".</summary>
        public double MaxGap;
        /// <summary>Two objects closer than this are considered touching.</summary>
        public double TouchTol;

        public GapOptions(double shortLength, double maxGap, double touchTol)
        {
            ShortLength = shortLength;
            MaxGap = maxGap;
            TouchTol = touchTol;
        }

        /// <summary>Builds drawing-unit options from thresholds given in metres.</summary>
        public static GapOptions FromMetres(double shortM, double maxGapM, double touchTolM, double unitsPerMetre)
        {
            return new GapOptions(shortM * unitsPerMetre, maxGapM * unitsPerMetre, touchTolM * unitsPerMetre);
        }
    }

    public enum FindingKind
    {
        /// <summary>Free end whose extension hits another object.</summary>
        ExtendToLine,
        /// <summary>Free end whose extension meets the extension of an object that also stops short (open corner).</summary>
        ExtendCorner,
        /// <summary>Free end followed by a collinear line, or by an arc on the same circle, after a small gap.</summary>
        ExtendCollinear,
        /// <summary>Whole object is no longer than the short length (includes zero length).</summary>
        TrimShortLine,
        /// <summary>Free end sticks out past a crossing by a short stub.</summary>
        TrimOvershoot
    }

    public struct GapFinding
    {
        /// <summary>Index of the flagged object in the input array.</summary>
        public int Line;
        public FindingKind Kind;
        /// <summary>Gap, stub or object length in drawing units (measured along arcs).</summary>
        public double Size;
        /// <summary>Location in drawing coordinates (the free end, or the middle of a tiny object).</summary>
        public double X, Y;
        /// <summary>Index of the other object involved (may be the same object), or -1.</summary>
        public int Other;

        public bool IsTrim
        {
            get { return Kind == FindingKind.TrimShortLine || Kind == FindingKind.TrimOvershoot; }
        }

        public static string Describe(FindingKind kind)
        {
            switch (kind)
            {
                case FindingKind.ExtendToLine: return "EXTEND to line";
                case FindingKind.ExtendCorner: return "EXTEND corner";
                case FindingKind.ExtendCollinear: return "EXTEND collinear";
                case FindingKind.TrimShortLine: return "TRIM short line";
                case FindingKind.TrimOvershoot: return "TRIM overshoot";
                default: return kind.ToString();
            }
        }
    }

    public enum LineMark { None = 0, Extend = 1, Trim = 2 }

    public sealed class GapResult
    {
        /// <summary>Number of objects analysed.</summary>
        public int LineCount;
        public bool[] NeedsExtend;
        public bool[] NeedsTrim;
        public List<GapFinding> Findings;
        /// <summary>Objects coloured blue: need extending and nothing else.</summary>
        public int ExtendCount;
        /// <summary>Objects coloured magenta: need trimming (some may also need extending).</summary>
        public int TrimCount;
        /// <summary>Objects that qualify for both; they are counted in TrimCount and coloured magenta.</summary>
        public int BothCount;
        /// <summary>Number of object ends that touch nothing.</summary>
        public int FreeEndCount;

        /// <summary>Colour category for an object. Trim wins over extend.</summary>
        public LineMark MarkOf(int line)
        {
            if (NeedsTrim[line]) return LineMark.Trim;
            if (NeedsExtend[line]) return LineMark.Extend;
            return LineMark.None;
        }
    }

    /// <summary>Unit conversion based on the AutoCAD INSUNITS codes.</summary>
    public static class GapUnits
    {
        /// <summary>
        /// Drawing units per metre for an INSUNITS code. Returns false for 0 (undefined)
        /// and for unit codes this tool does not handle.
        /// </summary>
        public static bool TryUnitsPerMetre(int insunits, out double unitsPerMetre, out string name)
        {
            switch (insunits)
            {
                case 1: unitsPerMetre = 1.0 / 0.0254; name = "inches"; return true;
                case 2: unitsPerMetre = 1.0 / 0.3048; name = "feet"; return true;
                case 3: unitsPerMetre = 1.0 / 1609.344; name = "miles"; return true;
                case 4: unitsPerMetre = 1000.0; name = "millimetres"; return true;
                case 5: unitsPerMetre = 100.0; name = "centimetres"; return true;
                case 6: unitsPerMetre = 1.0; name = "metres"; return true;
                case 7: unitsPerMetre = 0.001; name = "kilometres"; return true;
                case 10: unitsPerMetre = 1.0 / 0.9144; name = "yards"; return true;
                case 14: unitsPerMetre = 10.0; name = "decimetres"; return true;
                default:
                    unitsPerMetre = 1.0;
                    name = insunits == 0 ? "undefined" : "unsupported (INSUNITS=" + insunits + ")";
                    return false;
            }
        }
    }

    public static class GapEngine
    {
        /// <summary>Lines whose directions differ by less than this sine are treated as parallel.</summary>
        public const double ParallelSin = 1e-4;
        internal const double TwoPi = 2.0 * Math.PI;

        /// <summary>Analyses plain line segments (each one is its own object).</summary>
        public static GapResult Analyze(Seg2[] lines, GapOptions options)
        {
            if (lines == null) throw new ArgumentNullException("lines");
            GapCurve[] curves = new GapCurve[lines.Length];
            for (int i = 0; i < lines.Length; i++)
                curves[i] = GapCurve.FromLine(lines[i].X1, lines[i].Y1, lines[i].X2, lines[i].Y2);
            return Analyze(curves, options);
        }

        /// <summary>Analyses lines, arcs, circles and polylines together.</summary>
        public static GapResult Analyze(GapCurve[] curves, GapOptions options)
        {
            if (curves == null) throw new ArgumentNullException("curves");
            if (options == null) throw new ArgumentNullException("options");
            CheckThreshold(options.ShortLength, "ShortLength");
            CheckThreshold(options.MaxGap, "MaxGap");
            CheckThreshold(options.TouchTol, "TouchTol");

            Analyzer a = new Analyzer(curves, options);
            return a.Run();
        }

        private static void CheckThreshold(double v, string name)
        {
            if (double.IsNaN(v) || double.IsInfinity(v) || v < 0)
                throw new ArgumentOutOfRangeException(name, "Threshold must be a finite value >= 0.");
        }

        /// <summary>Angle in [0, 2*pi).</summary>
        internal static double Norm(double a)
        {
            a %= TwoPi;
            if (a < 0) a += TwoPi;
            if (a >= TwoPi) a = 0;
            return a;
        }

        private sealed class Analyzer
        {
            private readonly GapOptions opt;
            private readonly int m;                      // objects (curves)
            private readonly int n;                      // pieces
            private readonly double ox, oy;              // local origin (precision for large coordinates)

            // Pieces, relative to the origin. Segments use ux/uy; arcs use the centre fields.
            private readonly int[] cur;                  // owning curve
            private readonly bool[] isArc;
            private readonly double[] ax, ay, bx, by, len, ux, uy;
            private readonly double[] cx, cy, rad, a0, sw;

            // Curves.
            private readonly int[] first, cnt;
            private readonly bool[] closed, tiny;
            private readonly double[] clen;
            private readonly bool[] free;                // free[2*c] = start, free[2*c+1] = end

            private SegmentGrid grid;
            private readonly List<int> hits = new List<int>();
            private readonly List<int> walkPiece = new List<int>();
            private readonly List<double> walkBase = new List<double>();
            private readonly double[] hx = new double[2], hy = new double[2];

            /// <summary>Where a free end may grow: a straight ray, or the rest of an arc's circle.</summary>
            private struct Path
            {
                public int Piece;
                public bool Circ;
                public double X, Y, Dx, Dy;              // start point; ray direction
                public double Cx, Cy, R, Phi, Dir;       // circle: centre, radius, start angle, +1/-1
                public double Limit;
            }

            public Analyzer(GapCurve[] curves, GapOptions options)
            {
                opt = options;
                m = curves.Length;
                double tol = opt.TouchTol;

                // Validate, find the origin, and decide which pieces to keep. Pieces no longer than
                // the touch tolerance (duplicate vertices) carry no direction and are dropped, unless
                // a curve would be left with none.
                double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
                bool[][] keep = new bool[m][];
                int total = 0;
                for (int c = 0; c < m; c++)
                {
                    GapCurve g = curves[c];
                    if (g == null) throw new ArgumentException("Object " + c + " is null.");
                    GapCurve.Piece[] ps = g.Pieces;
                    keep[c] = new bool[ps.Length];
                    int kept = 0, longest = 0;
                    double longestLen = -1;
                    for (int k = 0; k < ps.Length; k++)
                    {
                        GapCurve.Piece p = ps[k];
                        if (!IsFinite(p.X1) || !IsFinite(p.Y1) || !IsFinite(p.X2) || !IsFinite(p.Y2) ||
                            (p.IsArc && (!IsFinite(p.Cx) || !IsFinite(p.Cy) || !IsFinite(p.R) ||
                                         !IsFinite(p.A0) || !IsFinite(p.Sweep))))
                            throw new ArgumentException("Object " + c + " has a non-finite coordinate.");
                        minX = Math.Min(minX, Math.Min(p.X1, p.X2));
                        minY = Math.Min(minY, Math.Min(p.Y1, p.Y2));
                        double l = PieceLength(p);
                        if (l > longestLen) { longestLen = l; longest = k; }
                        if (l > tol) { keep[c][k] = true; kept++; }
                    }
                    if (kept == 0) { keep[c][longest] = true; kept = 1; }
                    total += kept;
                }
                ox = m > 0 ? minX : 0.0;
                oy = m > 0 ? minY : 0.0;
                n = total;

                cur = new int[n]; isArc = new bool[n];
                ax = new double[n]; ay = new double[n]; bx = new double[n]; by = new double[n];
                len = new double[n]; ux = new double[n]; uy = new double[n];
                cx = new double[n]; cy = new double[n]; rad = new double[n]; a0 = new double[n]; sw = new double[n];
                first = new int[m]; cnt = new int[m];
                closed = new bool[m]; tiny = new bool[m]; clen = new double[m];
                free = new bool[2 * m];

                int q = 0;
                for (int c = 0; c < m; c++)
                {
                    GapCurve.Piece[] ps = curves[c].Pieces;
                    first[c] = q;
                    closed[c] = curves[c].Closed;
                    double sum = 0;
                    for (int k = 0; k < ps.Length; k++)
                    {
                        if (!keep[c][k]) continue;
                        GapCurve.Piece p = ps[k];
                        cur[q] = c;
                        isArc[q] = p.IsArc;
                        ax[q] = p.X1 - ox; ay[q] = p.Y1 - oy;
                        bx[q] = p.X2 - ox; by[q] = p.Y2 - oy;
                        double l = PieceLength(p);
                        len[q] = l;
                        if (p.IsArc)
                        {
                            cx[q] = p.Cx - ox; cy[q] = p.Cy - oy;
                            rad[q] = p.R; a0[q] = p.A0; sw[q] = p.Sweep;
                        }
                        else if (l > 0)
                        {
                            ux[q] = (bx[q] - ax[q]) / l;
                            uy[q] = (by[q] - ay[q]) / l;
                        }
                        sum += l;
                        q++;
                    }
                    cnt[c] = q - first[c];
                    clen[c] = sum;
                    tiny[c] = sum <= opt.ShortLength;
                }
            }

            private static double PieceLength(GapCurve.Piece p)
            {
                if (p.IsArc) return p.R * Math.Abs(p.Sweep);
                double dx = p.X2 - p.X1, dy = p.Y2 - p.Y1;
                return Math.Sqrt(dx * dx + dy * dy);
            }

            private int LastPiece(int c)
            {
                return first[c] + cnt[c] - 1;
            }

            private void EndPoint(int c, int end, out double px, out double py)
            {
                if (end == 0) { px = ax[first[c]]; py = ay[first[c]]; }
                else { int k = LastPiece(c); px = bx[k]; py = by[k]; }
            }

            public GapResult Run()
            {
                GapResult r = new GapResult();
                r.LineCount = m;
                r.NeedsExtend = new bool[m];
                r.NeedsTrim = new bool[m];
                r.Findings = new List<GapFinding>();
                if (m == 0) return r;

                BuildGrid();

                // Pass 1: which ends touch nothing? Tiny objects do count as touching here.
                for (int c = 0; c < m; c++)
                {
                    if (closed[c]) continue;
                    free[2 * c] = !Touches(c, 0);
                    free[2 * c + 1] = !Touches(c, 1);
                    if (free[2 * c]) r.FreeEndCount++;
                    if (free[2 * c + 1]) r.FreeEndCount++;
                }

                // Pass 2: classify.
                for (int c = 0; c < m; c++)
                {
                    if (tiny[c])
                    {
                        double sx, sy, ex, ey;
                        EndPoint(c, 0, out sx, out sy);
                        EndPoint(c, 1, out ex, out ey);
                        r.NeedsTrim[c] = true;
                        r.Findings.Add(MakeFinding(c, FindingKind.TrimShortLine, clen[c],
                            (sx + ex) * 0.5, (sy + ey) * 0.5, -1));
                        continue;
                    }
                    if (closed[c]) continue;

                    for (int end = 0; end < 2; end++)
                    {
                        if (!free[2 * c + end]) continue;
                        double px, py;
                        EndPoint(c, end, out px, out py);

                        FindingKind kind;
                        int other;
                        double gap;
                        if (FindExtension(c, end, out gap, out kind, out other))
                        {
                            r.NeedsExtend[c] = true;
                            r.Findings.Add(MakeFinding(c, kind, gap, px, py, other));
                        }

                        double stub;
                        if (FindOvershoot(c, end, out stub, out other))
                        {
                            r.NeedsTrim[c] = true;
                            r.Findings.Add(MakeFinding(c, FindingKind.TrimOvershoot, stub, px, py, other));
                        }
                    }
                }

                for (int c = 0; c < m; c++)
                {
                    if (r.NeedsTrim[c])
                    {
                        r.TrimCount++;
                        if (r.NeedsExtend[c]) r.BothCount++;
                    }
                    else if (r.NeedsExtend[c])
                    {
                        r.ExtendCount++;
                    }
                }
                return r;
            }

            private GapFinding MakeFinding(int c, FindingKind kind, double size, double lx, double ly, int other)
            {
                GapFinding f = new GapFinding();
                f.Line = c;
                f.Kind = kind;
                f.Size = size;
                f.X = lx + ox;
                f.Y = ly + oy;
                f.Other = other;
                return f;
            }

            // ------------------------------------------------------------ spatial grid

            private void BuildGrid()
            {
                double maxX = 0, maxY = 0, sumLen = 0, sumSpan = 0;
                int counted = 0;
                for (int q = 0; q < n; q++)
                {
                    double x0, y0, x1, y1;
                    PieceBox(q, out x0, out y0, out x1, out y1);
                    maxX = Math.Max(maxX, x1);
                    maxY = Math.Max(maxY, y1);
                    sumSpan += (x1 - x0) + (y1 - y0);
                    if (!tiny[cur[q]]) { sumLen += len[q]; counted++; }
                }
                double extent = Math.Max(maxX, maxY);
                double meanLen = counted > 0 ? sumLen / counted : 0.0;

                // Largest radius any query uses: a corner search reaches 2 * MaxGap.
                double reach = Math.Max(2.0 * opt.MaxGap, opt.ShortLength) + 2.0 * opt.TouchTol;
                double cell = Math.Max(reach, meanLen);
                cell = Math.Max(cell, extent * 1e-7);   // keep cell indices inside int range
                if (!(cell > 0)) cell = 1.0;            // everything degenerate

                // Cap the number of grid entries; each piece occupies about span/cell + 1 cells.
                double estimate = sumSpan / cell + n;
                double limit = 8.0 * n + 1e6;
                if (estimate > limit) cell *= estimate / limit;

                grid = new SegmentGrid(cell, n);
                for (int q = 0; q < n; q++)
                {
                    if (!isArc[q])
                    {
                        grid.Add(q, ax[q], ay[q], bx[q], by[q]);
                        continue;
                    }
                    // Arcs: split into parts of at most 45 degrees and about one cell long, and
                    // register each part's box (chord box grown by the sagitta).
                    double asw = Math.Abs(sw[q]);
                    double lo = sw[q] >= 0 ? a0[q] : a0[q] + sw[q];
                    int parts = (int)Math.Ceiling(Math.Max(asw / (Math.PI / 4), len[q] / cell));
                    if (parts < 1) parts = 1;
                    if (parts > 100000) parts = 100000;
                    double step = asw / parts;
                    double pad = rad[q] * (1 - Math.Cos(step * 0.5)) + cell * 1e-9;
                    for (int i = 0; i < parts; i++)
                    {
                        double s0 = lo + i * step, s1 = lo + (i + 1) * step;
                        double x0 = cx[q] + rad[q] * Math.Cos(s0), y0 = cy[q] + rad[q] * Math.Sin(s0);
                        double x1 = cx[q] + rad[q] * Math.Cos(s1), y1 = cy[q] + rad[q] * Math.Sin(s1);
                        grid.AddBox(q, Math.Min(x0, x1) - pad, Math.Min(y0, y1) - pad,
                                       Math.Max(x0, x1) + pad, Math.Max(y0, y1) + pad);
                    }
                }
            }

            private void PieceBox(int q, out double x0, out double y0, out double x1, out double y1)
            {
                x0 = Math.Min(ax[q], bx[q]); x1 = Math.Max(ax[q], bx[q]);
                y0 = Math.Min(ay[q], by[q]); y1 = Math.Max(ay[q], by[q]);
                if (!isArc[q]) return;
                for (int i = 0; i < 4; i++)
                {
                    double ang = i * Math.PI * 0.5;
                    if (!InSweep(q, ang)) continue;
                    double px = cx[q] + rad[q] * Math.Cos(ang), py = cy[q] + rad[q] * Math.Sin(ang);
                    x0 = Math.Min(x0, px); x1 = Math.Max(x1, px);
                    y0 = Math.Min(y0, py); y1 = Math.Max(y1, py);
                }
            }

            // ------------------------------------------------------------ piece geometry

            /// <summary>Is the angle inside the arc's sweep?</summary>
            private bool InSweep(int q, double angle)
            {
                double asw = Math.Abs(sw[q]);
                if (asw >= TwoPi) return true;
                double s = sw[q] >= 0 ? 1.0 : -1.0;
                return Norm(s * (angle - a0[q])) <= asw;
            }

            /// <summary>
            /// Signed distance from the piece's start to the point (projected onto the piece's line
            /// or circle), in drawing order. Below 0 = before the start, above len = past the end.
            /// </summary>
            private double PosOnPiece(int q, double x, double y)
            {
                if (!isArc[q]) return (x - ax[q]) * ux[q] + (y - ay[q]) * uy[q];
                double asw = Math.Abs(sw[q]);
                double s = sw[q] >= 0 ? 1.0 : -1.0;
                double d = Norm(s * (Math.Atan2(y - cy[q], x - cx[q]) - a0[q]));
                if (d > asw + (TwoPi - asw) * 0.5) d -= TwoPi;   // nearer to the start than the end
                return rad[q] * d;
            }

            private double DistSq(int q, double px, double py)
            {
                if (!isArc[q])
                {
                    double vx = bx[q] - ax[q], vy = by[q] - ay[q];
                    double wx = px - ax[q], wy = py - ay[q];
                    double l2 = vx * vx + vy * vy;
                    double t = l2 > 0 ? (wx * vx + wy * vy) / l2 : 0.0;
                    if (t < 0) t = 0; else if (t > 1) t = 1;
                    double ex = ax[q] + t * vx - px, ey = ay[q] + t * vy - py;
                    return ex * ex + ey * ey;
                }
                double dx = px - cx[q], dy = py - cy[q];
                if (InSweep(q, Math.Atan2(dy, dx)))
                {
                    double e = Math.Sqrt(dx * dx + dy * dy) - rad[q];
                    return e * e;
                }
                double ax2 = px - ax[q], ay2 = py - ay[q], bx2 = px - bx[q], by2 = py - by[q];
                return Math.Min(ax2 * ax2 + ay2 * ay2, bx2 * bx2 + by2 * by2);
            }

            /// <summary>Is this piece end also an end of its (open) curve, and free?</summary>
            private bool IsFreeCurveEnd(int q, int pieceEnd)
            {
                int c = cur[q];
                if (closed[c]) return false;
                if (pieceEnd == 0) return q == first[c] && free[2 * c];
                return q == LastPiece(c) && free[2 * c + 1];
            }

            /// <summary>
            /// Intersections of two carriers (the infinite line or full circle of a piece or path).
            /// A line is a point plus a unit direction; a circle is a centre plus a radius. Writes up
            /// to two points into hx/hy and returns how many, or -1 for parallel lines and for
            /// circles that coincide (callers handle those separately). Near-tangent contacts
            /// (within the touch tolerance) count as one point.
            /// </summary>
            private int Intersect(bool circA, double xA, double yA, double dxA, double dyA, double rA,
                                  bool circB, double xB, double yB, double dxB, double dyB, double rB)
            {
                double tol = opt.TouchTol;
                if (!circA && !circB)
                {
                    double cross = dxA * dyB - dyA * dxB;
                    if (Math.Abs(cross) < ParallelSin) return -1;
                    double t = ((xB - xA) * dyB - (yB - yA) * dxB) / cross;
                    hx[0] = xA + t * dxA;
                    hy[0] = yA + t * dyA;
                    return 1;
                }
                if (circA && circB)
                {
                    double dx = xB - xA, dy = yB - yA;
                    double d = Math.Sqrt(dx * dx + dy * dy);
                    if (d <= tol) return Math.Abs(rA - rB) <= tol ? -1 : 0;
                    if (d > rA + rB + tol || d < Math.Abs(rA - rB) - tol) return 0;
                    double ex = dx / d, ey = dy / d;
                    double a = (d * d + rA * rA - rB * rB) / (2 * d);
                    double h2 = rA * rA - a * a;
                    double mx = xA + a * ex, my = yA + a * ey;
                    if (h2 <= 0 || Math.Abs(d - (rA + rB)) <= tol || Math.Abs(d - Math.Abs(rA - rB)) <= tol)
                    {
                        hx[0] = mx; hy[0] = my;
                        return 1;
                    }
                    double h = Math.Sqrt(h2);
                    hx[0] = mx - h * ey; hy[0] = my + h * ex;
                    hx[1] = mx + h * ey; hy[1] = my - h * ex;
                    return 2;
                }

                // One line, one circle.
                double lx, ly, ldx, ldy, ccx, ccy, r;
                if (circA) { ccx = xA; ccy = yA; r = rA; lx = xB; ly = yB; ldx = dxB; ldy = dyB; }
                else { ccx = xB; ccy = yB; r = rB; lx = xA; ly = yA; ldx = dxA; ldy = dyA; }
                double wx = ccx - lx, wy = ccy - ly;
                double tc = wx * ldx + wy * ldy;                 // foot of the perpendicular from the centre
                double hh = wx * wx + wy * wy - tc * tc;
                double dist = hh > 0 ? Math.Sqrt(hh) : 0.0;
                if (dist > r + tol) return 0;
                if (r - dist <= tol)
                {
                    hx[0] = lx + tc * ldx; hy[0] = ly + tc * ldy;
                    return 1;
                }
                double half = Math.Sqrt((r - dist) * (r + dist));
                hx[0] = lx + (tc - half) * ldx; hy[0] = ly + (tc - half) * ldy;
                hx[1] = lx + (tc + half) * ldx; hy[1] = ly + (tc + half) * ldy;
                return 2;
            }

            private int IntersectPieces(int p, int q)
            {
                return Intersect(isArc[p], isArc[p] ? cx[p] : ax[p], isArc[p] ? cy[p] : ay[p], ux[p], uy[p], rad[p],
                                 isArc[q], isArc[q] ? cx[q] : ax[q], isArc[q] ? cy[q] : ay[q], ux[q], uy[q], rad[q]);
            }

            private int IntersectPath(ref Path path, int q)
            {
                if (path.Circ)
                    return Intersect(true, path.Cx, path.Cy, 0, 0, path.R,
                                     isArc[q], isArc[q] ? cx[q] : ax[q], isArc[q] ? cy[q] : ay[q], ux[q], uy[q], rad[q]);
                return Intersect(false, path.X, path.Y, path.Dx, path.Dy, 0,
                                 isArc[q], isArc[q] ? cx[q] : ax[q], isArc[q] ? cy[q] : ay[q], ux[q], uy[q], rad[q]);
            }

            /// <summary>Distance along the path from its start to a point on its carrier.</summary>
            private static double PathParam(ref Path path, double x, double y)
            {
                if (!path.Circ) return (x - path.X) * path.Dx + (y - path.Y) * path.Dy;
                return path.R * Norm(path.Dir * (Math.Atan2(y - path.Cy, x - path.Cx) - path.Phi));
            }

            /// <summary>
            /// How a free end grows: straight ends extend along their direction, arc ends extend
            /// along their circle (as AutoCAD's EXTEND does), never past the arc's own other end.
            /// </summary>
            private Path MakePath(int c, int end)
            {
                Path p = new Path();
                int k = end == 0 ? first[c] : LastPiece(c);
                p.Piece = k;
                EndPoint(c, end, out p.X, out p.Y);
                if (!isArc[k])
                {
                    p.Dx = end == 0 ? -ux[k] : ux[k];
                    p.Dy = end == 0 ? -uy[k] : uy[k];
                    p.Limit = opt.MaxGap;
                    return p;
                }
                double s = sw[k] >= 0 ? 1.0 : -1.0;
                p.Circ = true;
                p.Cx = cx[k]; p.Cy = cy[k]; p.R = rad[k];
                p.Phi = Math.Atan2(p.Y - p.Cy, p.X - p.Cx);
                p.Dir = end == 1 ? s : -s;
                p.Limit = Math.Min(opt.MaxGap, rad[k] * (TwoPi - Math.Abs(sw[k])));
                return p;
            }

            // ------------------------------------------------------------ rules

            /// <summary>Is this end within TouchTol of any other piece?</summary>
            private bool Touches(int c, int end)
            {
                double tol = opt.TouchTol;
                double tol2 = tol * tol;
                double px, py;
                EndPoint(c, end, out px, out py);

                // Skip the end piece itself (and any further own pieces that are within tol of it).
                int step = end == 0 ? 1 : -1;
                int k = end == 0 ? first[c] : LastPiece(c);
                int k2 = k;
                double acc = len[k];
                while (acc <= 2 * tol)
                {
                    int next = k2 + step;
                    if (next < first[c] || next > LastPiece(c)) break;
                    k2 = next;
                    acc += len[k2];
                }
                int skipLo = Math.Min(k, k2), skipHi = Math.Max(k, k2);

                grid.Query(px - tol, py - tol, px + tol, py + tol, hits);
                for (int h = 0; h < hits.Count; h++)
                {
                    int q = hits[h];
                    if (cur[q] == c && q >= skipLo && q <= skipHi) continue;
                    if (DistSq(q, px, py) <= tol2) return true;
                }
                return false;
            }

            /// <summary>Nearest extension target for a free end.</summary>
            private bool FindExtension(int c, int end, out double best, out FindingKind bestKind, out int bestOther)
            {
                double maxGap = opt.MaxGap;
                double tol = opt.TouchTol;
                best = double.PositiveInfinity;
                bestKind = FindingKind.ExtendToLine;
                bestOther = -1;
                if (maxGap <= 0) return false;

                Path path = MakePath(c, end);
                double limit = path.Limit * (1 + 1e-12);
                double gapLimit = maxGap * (1 + 1e-12);

                // An arc that almost closes on itself: its two ends face each other across the gap.
                if (path.Circ && cnt[c] == 1)
                {
                    double g = rad[path.Piece] * (TwoPi - Math.Abs(sw[path.Piece]));
                    if (g > tol && g <= gapLimit && free[2 * c + (1 - end)])
                    {
                        best = g; bestKind = FindingKind.ExtendCollinear; bestOther = c;
                    }
                }

                double r = 2.0 * maxGap + tol;
                grid.Query(path.X - r, path.Y - r, path.X + r, path.Y + r, hits);
                for (int h = 0; h < hits.Count; h++)
                {
                    int q = hits[h];
                    if (q == path.Piece || tiny[cur[q]]) continue;   // tiny objects are never targets

                    int count = IntersectPath(ref path, q);
                    if (count < 0)
                    {
                        double along;
                        if (CollinearGap(ref path, q, limit, out along) && along < best)
                        {
                            best = along; bestKind = FindingKind.ExtendCollinear; bestOther = cur[q];
                        }
                        continue;
                    }

                    for (int i = 0; i < count; i++)
                    {
                        double t = PathParam(ref path, hx[i], hy[i]);
                        if (t <= tol || t > limit || t >= best) continue;
                        double pos = PosOnPiece(q, hx[i], hy[i]);
                        if (pos >= -tol && pos <= len[q] + tol)
                        {
                            // (a) the extension hits the piece itself.
                            best = t; bestKind = FindingKind.ExtendToLine; bestOther = cur[q];
                        }
                        else
                        {
                            // (b) it meets the extension of q beyond one of q's ends; that end must
                            // be a free end of q's object.
                            bool beyondEnd = pos > len[q];
                            double overrun = beyondEnd ? pos - len[q] : -pos;
                            if (overrun <= gapLimit && IsFreeCurveEnd(q, beyondEnd ? 1 : 0))
                            {
                                best = t; bestKind = FindingKind.ExtendCorner; bestOther = cur[q];
                            }
                        }
                    }
                }
                return bestOther >= 0;
            }

            /// <summary>
            /// (c) Collinear gap: a parallel line straight ahead, or an arc on the same circle.
            /// Only the near end of q matters.
            /// </summary>
            private bool CollinearGap(ref Path path, int q, double limit, out double along)
            {
                double tol = opt.TouchTol;
                if (path.Circ)
                {
                    along = Math.Min(PathParam(ref path, ax[q], ay[q]), PathParam(ref path, bx[q], by[q]));
                    return along > tol && along <= limit;
                }

                double alongA = (ax[q] - path.X) * path.Dx + (ay[q] - path.Y) * path.Dy;
                double alongB = (bx[q] - path.X) * path.Dx + (by[q] - path.Y) * path.Dy;
                double qx, qy;
                if (alongA <= alongB) { qx = ax[q]; qy = ay[q]; along = alongA; }
                else { qx = bx[q]; qy = by[q]; along = alongB; }
                if (along <= 0 || along > limit) return false;
                double perp = Math.Abs((qx - path.X) * path.Dy - (qy - path.Y) * path.Dx);
                return perp <= Math.Max(tol, ParallelSin * along);
            }

            /// <summary>
            /// Walks inward from a free end (along the object, across vertices) and looks for the
            /// nearest crossing with another object. Reports a stub when TouchTol &lt; stub &lt;= ShortLength.
            /// </summary>
            private bool FindOvershoot(int c, int end, out double stub, out int other)
            {
                double shortLen = opt.ShortLength;
                double tol = opt.TouchTol;
                stub = double.PositiveInfinity;
                other = -1;
                if (shortLen <= tol) return false;

                double reach = Math.Min(shortLen, clen[c]);
                double reachLimit = reach * (1 + 1e-12);
                double px, py;
                EndPoint(c, end, out px, out py);

                // Pieces covered by the walk, with the walk distance at which each one starts.
                walkPiece.Clear();
                walkBase.Clear();
                int step = end == 0 ? 1 : -1;
                int k = end == 0 ? first[c] : LastPiece(c);
                double walked = 0;
                while (true)
                {
                    walkPiece.Add(k);
                    walkBase.Add(walked);
                    walked += len[k];
                    if (walked >= reach) break;
                    k += step;
                    if (k < first[c] || k > LastPiece(c)) break;
                }
                // Own pieces next to the walked ones share a vertex with them: not crossings.
                int skipLo = Math.Min(walkPiece[0], k) - 1, skipHi = Math.Max(walkPiece[0], k) + 1;

                double r = reach + tol;
                grid.Query(px - r, py - r, px + r, py + r, hits);
                for (int h = 0; h < hits.Count; h++)
                {
                    int q = hits[h];
                    if (tiny[cur[q]]) continue;
                    if (cur[q] == c && q >= skipLo && q <= skipHi) continue;

                    for (int w = 0; w < walkPiece.Count; w++)
                    {
                        int p = walkPiece[w];
                        int count = IntersectPieces(p, q);
                        for (int i = 0; i < count; i++)
                        {
                            double posQ = PosOnPiece(q, hx[i], hy[i]);
                            if (posQ < -tol || posQ > len[q] + tol) continue;
                            double posP = PosOnPiece(p, hx[i], hy[i]);
                            if (posP < -tol || posP > len[p] + tol) continue;
                            double along = end == 0 ? posP : len[p] - posP;
                            double t = walkBase[w] + Math.Max(0.0, along);
                            if (t <= tol || t > reachLimit || t >= stub) continue;
                            stub = t;
                            other = cur[q];
                        }
                    }
                }
                return other >= 0;
            }

            private static bool IsFinite(double v)
            {
                return !double.IsNaN(v) && !double.IsInfinity(v);
            }
        }

        /// <summary>
        /// Uniform hash grid. Each segment is registered in every cell it passes through
        /// (grid traversal, not its bounding box), so long diagonal lines stay cheap.
        /// Arcs are registered part by part with small boxes.
        /// </summary>
        private sealed class SegmentGrid
        {
            private readonly double cell;
            private readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
            private readonly int[] stamp;
            private int stampId;

            public SegmentGrid(double cellSize, int count)
            {
                cell = cellSize;
                stamp = new int[count];
            }

            private static long Key(int ix, int iy)
            {
                return ((long)ix << 32) | (uint)iy;
            }

            private int Index(double v)
            {
                return (int)Math.Floor(v / cell);
            }

            private void Put(int ix, int iy, int id)
            {
                long key = Key(ix, iy);
                List<int> list;
                if (!cells.TryGetValue(key, out list))
                {
                    list = new List<int>(4);
                    cells.Add(key, list);
                }
                if (list.Count > 0 && list[list.Count - 1] == id) return;   // same arc, neighbouring part
                list.Add(id);
            }

            public void Add(int id, double x1, double y1, double x2, double y2)
            {
                int cx = Index(x1), cy = Index(y1);
                int ex = Index(x2), ey = Index(y2);
                int stepX = ex > cx ? 1 : (ex < cx ? -1 : 0);
                int stepY = ey > cy ? 1 : (ey < cy ? -1 : 0);
                int remX = Math.Abs(ex - cx), remY = Math.Abs(ey - cy);

                double dx = x2 - x1, dy = y2 - y1;
                double tMaxX = double.PositiveInfinity, tMaxY = double.PositiveInfinity;
                double tDeltaX = double.PositiveInfinity, tDeltaY = double.PositiveInfinity;
                if (stepX != 0)
                {
                    double edge = (stepX > 0 ? cx + 1 : cx) * cell;
                    tMaxX = (edge - x1) / dx;
                    tDeltaX = cell / Math.Abs(dx);
                }
                if (stepY != 0)
                {
                    double edge = (stepY > 0 ? cy + 1 : cy) * cell;
                    tMaxY = (edge - y1) / dy;
                    tDeltaY = cell / Math.Abs(dy);
                }

                Put(cx, cy, id);
                // Exactly remX + remY steps, so rounding can never run past the end cell.
                while (remX > 0 || remY > 0)
                {
                    bool stepInX = remY == 0 || (remX > 0 && tMaxX < tMaxY);
                    if (stepInX) { cx += stepX; tMaxX += tDeltaX; remX--; }
                    else { cy += stepY; tMaxY += tDeltaY; remY--; }
                    Put(cx, cy, id);
                }
            }

            /// <summary>Registers the piece in every cell overlapping the box.</summary>
            public void AddBox(int id, double minX, double minY, double maxX, double maxY)
            {
                int x0 = Index(minX), x1 = Index(maxX);
                int y0 = Index(minY), y1 = Index(maxY);
                for (int ix = x0; ix <= x1; ix++)
                    for (int iy = y0; iy <= y1; iy++)
                        Put(ix, iy, id);
            }

            /// <summary>Collects each piece registered in cells overlapping the box, once.</summary>
            public void Query(double minX, double minY, double maxX, double maxY, List<int> result)
            {
                result.Clear();
                stampId++;
                if (stampId == int.MaxValue)
                {
                    Array.Clear(stamp, 0, stamp.Length);
                    stampId = 1;
                }
                double pad = cell * 1e-9;
                int x0 = Index(minX - pad), x1 = Index(maxX + pad);
                int y0 = Index(minY - pad), y1 = Index(maxY + pad);
                for (int ix = x0; ix <= x1; ix++)
                {
                    for (int iy = y0; iy <= y1; iy++)
                    {
                        List<int> list;
                        if (!cells.TryGetValue(Key(ix, iy), out list)) continue;
                        for (int k = 0; k < list.Count; k++)
                        {
                            int id = list[k];
                            if (stamp[id] == stampId) continue;
                            stamp[id] = stampId;
                            result.Add(id);
                        }
                    }
                }
            }
        }
    }
}
