// GapEngine.cs - 2D line gap / overshoot analysis.
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

    /// <summary>Analysis thresholds, all in drawing units.</summary>
    public sealed class GapOptions
    {
        /// <summary>Lines this long or shorter are "tiny"; overshoot stubs up to this length are trimmed.</summary>
        public double ShortLength;
        /// <summary>Largest gap that is reported as "needs extending".</summary>
        public double MaxGap;
        /// <summary>Two lines closer than this are considered touching.</summary>
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
        /// <summary>Free end whose outward ray hits another line.</summary>
        ExtendToLine,
        /// <summary>Free end whose ray hits the extension of a line that also stops short (open corner).</summary>
        ExtendCorner,
        /// <summary>Free end followed by a collinear line after a small gap.</summary>
        ExtendCollinear,
        /// <summary>Whole line is no longer than the short length (includes zero length).</summary>
        TrimShortLine,
        /// <summary>Free end sticks out past a crossing by a short stub.</summary>
        TrimOvershoot
    }

    public struct GapFinding
    {
        /// <summary>Index of the flagged line in the input array.</summary>
        public int Line;
        public FindingKind Kind;
        /// <summary>Gap, stub or line length in drawing units.</summary>
        public double Size;
        /// <summary>Location in drawing coordinates (the free end, or the midpoint of a tiny line).</summary>
        public double X, Y;
        /// <summary>Index of the other line involved, or -1.</summary>
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
        public int LineCount;
        public bool[] NeedsExtend;
        public bool[] NeedsTrim;
        public List<GapFinding> Findings;
        /// <summary>Lines coloured blue: need extending and nothing else.</summary>
        public int ExtendCount;
        /// <summary>Lines coloured magenta: need trimming (some may also need extending).</summary>
        public int TrimCount;
        /// <summary>Lines that qualify for both; they are counted in TrimCount and coloured magenta.</summary>
        public int BothCount;
        /// <summary>Number of line endpoints that touch no other line.</summary>
        public int FreeEndCount;

        /// <summary>Colour category for a line. Trim wins over extend.</summary>
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

        public static GapResult Analyze(Seg2[] lines, GapOptions options)
        {
            if (lines == null) throw new ArgumentNullException("lines");
            if (options == null) throw new ArgumentNullException("options");
            CheckThreshold(options.ShortLength, "ShortLength");
            CheckThreshold(options.MaxGap, "MaxGap");
            CheckThreshold(options.TouchTol, "TouchTol");

            Analyzer a = new Analyzer(lines, options);
            return a.Run();
        }

        private static void CheckThreshold(double v, string name)
        {
            if (double.IsNaN(v) || double.IsInfinity(v) || v < 0)
                throw new ArgumentOutOfRangeException(name, "Threshold must be a finite value >= 0.");
        }

        private sealed class Analyzer
        {
            private readonly int n;
            private readonly GapOptions opt;
            private readonly double ox, oy;          // local origin (improves precision for large coordinates)
            private readonly double[] ax, ay, bx, by; // endpoints relative to origin
            private readonly double[] len, ux, uy;   // length and unit direction A->B
            private readonly bool[] tiny;
            private readonly bool[] free;            // free[2*i] = start point, free[2*i+1] = end point
            private SegmentGrid grid;
            private readonly List<int> hits = new List<int>();

            public Analyzer(Seg2[] lines, GapOptions options)
            {
                n = lines.Length;
                opt = options;
                ax = new double[n]; ay = new double[n]; bx = new double[n]; by = new double[n];
                len = new double[n]; ux = new double[n]; uy = new double[n];
                tiny = new bool[n];
                free = new bool[2 * n];

                double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
                for (int i = 0; i < n; i++)
                {
                    Seg2 s = lines[i];
                    if (!IsFinite(s.X1) || !IsFinite(s.Y1) || !IsFinite(s.X2) || !IsFinite(s.Y2))
                        throw new ArgumentException("Line " + i + " has a non-finite coordinate.");
                    minX = Math.Min(minX, Math.Min(s.X1, s.X2));
                    minY = Math.Min(minY, Math.Min(s.Y1, s.Y2));
                }
                ox = n > 0 ? minX : 0.0;
                oy = n > 0 ? minY : 0.0;

                for (int i = 0; i < n; i++)
                {
                    Seg2 s = lines[i];
                    ax[i] = s.X1 - ox; ay[i] = s.Y1 - oy;
                    bx[i] = s.X2 - ox; by[i] = s.Y2 - oy;
                    double dx = bx[i] - ax[i], dy = by[i] - ay[i];
                    double l = Math.Sqrt(dx * dx + dy * dy);
                    len[i] = l;
                    if (l > 0) { ux[i] = dx / l; uy[i] = dy / l; }
                    tiny[i] = l <= opt.ShortLength;
                }
            }

            public GapResult Run()
            {
                GapResult r = new GapResult();
                r.LineCount = n;
                r.NeedsExtend = new bool[n];
                r.NeedsTrim = new bool[n];
                r.Findings = new List<GapFinding>();
                if (n == 0) return r;

                BuildGrid();

                // Pass 1: which endpoints touch nothing? Tiny lines do count as touching here.
                for (int i = 0; i < n; i++)
                {
                    free[2 * i] = !Touches(i, ax[i], ay[i]);
                    free[2 * i + 1] = !Touches(i, bx[i], by[i]);
                    if (free[2 * i]) r.FreeEndCount++;
                    if (free[2 * i + 1]) r.FreeEndCount++;
                }

                // Pass 2: classify.
                for (int i = 0; i < n; i++)
                {
                    if (tiny[i])
                    {
                        r.NeedsTrim[i] = true;
                        r.Findings.Add(MakeFinding(i, FindingKind.TrimShortLine, len[i],
                            (ax[i] + bx[i]) * 0.5, (ay[i] + by[i]) * 0.5, -1));
                        continue;
                    }

                    for (int end = 0; end < 2; end++)
                    {
                        if (!free[2 * i + end]) continue;
                        double px = end == 0 ? ax[i] : bx[i];
                        double py = end == 0 ? ay[i] : by[i];
                        // Outward direction: away from the other endpoint.
                        double dx = end == 0 ? -ux[i] : ux[i];
                        double dy = end == 0 ? -uy[i] : uy[i];

                        FindingKind kind;
                        int other;
                        double gap;
                        if (FindExtension(i, px, py, dx, dy, out gap, out kind, out other))
                        {
                            r.NeedsExtend[i] = true;
                            r.Findings.Add(MakeFinding(i, kind, gap, px, py, other));
                        }

                        double stub;
                        if (FindOvershoot(i, px, py, -dx, -dy, out stub, out other))
                        {
                            r.NeedsTrim[i] = true;
                            r.Findings.Add(MakeFinding(i, FindingKind.TrimOvershoot, stub, px, py, other));
                        }
                    }
                }

                for (int i = 0; i < n; i++)
                {
                    if (r.NeedsTrim[i])
                    {
                        r.TrimCount++;
                        if (r.NeedsExtend[i]) r.BothCount++;
                    }
                    else if (r.NeedsExtend[i])
                    {
                        r.ExtendCount++;
                    }
                }
                return r;
            }

            private GapFinding MakeFinding(int line, FindingKind kind, double size, double lx, double ly, int other)
            {
                GapFinding f = new GapFinding();
                f.Line = line;
                f.Kind = kind;
                f.Size = size;
                f.X = lx + ox;
                f.Y = ly + oy;
                f.Other = other;
                return f;
            }

            private void BuildGrid()
            {
                double maxX = 0, maxY = 0, sumLen = 0, sumSpan = 0;
                int counted = 0;
                for (int i = 0; i < n; i++)
                {
                    maxX = Math.Max(maxX, Math.Max(ax[i], bx[i]));
                    maxY = Math.Max(maxY, Math.Max(ay[i], by[i]));
                    sumSpan += Math.Abs(bx[i] - ax[i]) + Math.Abs(by[i] - ay[i]);
                    if (!tiny[i]) { sumLen += len[i]; counted++; }
                }
                double extent = Math.Max(maxX, maxY);
                double meanLen = counted > 0 ? sumLen / counted : 0.0;

                // Largest radius any query uses: a corner search reaches 2 * MaxGap.
                double reach = Math.Max(2.0 * opt.MaxGap, opt.ShortLength) + 2.0 * opt.TouchTol;
                double cell = Math.Max(reach, meanLen);
                cell = Math.Max(cell, extent * 1e-7);   // keep cell indices inside int range
                if (!(cell > 0)) cell = 1.0;            // everything degenerate

                // Cap the number of grid entries; each segment occupies about span/cell + 1 cells.
                double estimate = sumSpan / cell + n;
                double limit = 8.0 * n + 1e6;
                if (estimate > limit) cell *= estimate / limit;

                grid = new SegmentGrid(cell, n);
                for (int i = 0; i < n; i++) grid.Add(i, ax[i], ay[i], bx[i], by[i]);
            }

            /// <summary>Is the point within TouchTol of any line other than 'self'?</summary>
            private bool Touches(int self, double px, double py)
            {
                double tol = opt.TouchTol;
                double tol2 = tol * tol;
                grid.Query(px - tol, py - tol, px + tol, py + tol, hits);
                for (int k = 0; k < hits.Count; k++)
                {
                    int j = hits[k];
                    if (j == self) continue;
                    if (DistSqToSeg(j, px, py) <= tol2) return true;
                }
                return false;
            }

            private double DistSqToSeg(int j, double px, double py)
            {
                double vx = bx[j] - ax[j], vy = by[j] - ay[j];
                double wx = px - ax[j], wy = py - ay[j];
                double l2 = vx * vx + vy * vy;
                double t = l2 > 0 ? (wx * vx + wy * vy) / l2 : 0.0;
                if (t < 0) t = 0; else if (t > 1) t = 1;
                double cx = ax[j] + t * vx - px, cy = ay[j] + t * vy - py;
                return cx * cx + cy * cy;
            }

            /// <summary>
            /// Nearest extension target for a free end at P with outward unit direction D.
            /// </summary>
            private bool FindExtension(int self, double px, double py, double dx, double dy,
                                       out double best, out FindingKind bestKind, out int bestOther)
            {
                double maxGap = opt.MaxGap;
                double tol = opt.TouchTol;
                double limit = maxGap * (1 + 1e-12);
                best = double.PositiveInfinity;
                bestKind = FindingKind.ExtendToLine;
                bestOther = -1;
                if (maxGap <= 0) return false;

                double r = 2.0 * maxGap + tol;
                grid.Query(px - r, py - r, px + r, py + r, hits);
                for (int k = 0; k < hits.Count; k++)
                {
                    int j = hits[k];
                    if (j == self || tiny[j]) continue;   // tiny lines are never targets

                    double ex = ux[j], ey = uy[j];
                    double cross = dx * ey - dy * ex;      // sin of the angle between the lines

                    if (Math.Abs(cross) < ParallelSin)
                    {
                        // (c) collinear gap. Only the near endpoint of j matters.
                        double alongA = (ax[j] - px) * dx + (ay[j] - py) * dy;
                        double alongB = (bx[j] - px) * dx + (by[j] - py) * dy;
                        double qx, qy, along;
                        if (alongA <= alongB) { qx = ax[j]; qy = ay[j]; along = alongA; }
                        else { qx = bx[j]; qy = by[j]; along = alongB; }
                        if (along <= 0 || along > limit) continue;
                        double perp = Math.Abs((qx - px) * dy - (qy - py) * dx);
                        if (perp > Math.Max(tol, ParallelSin * along)) continue;
                        if (along < best)
                        {
                            best = along; bestKind = FindingKind.ExtendCollinear; bestOther = j;
                        }
                        continue;
                    }

                    // Ray P + t*D against line A_j + s*E (E unit, s in [0, len_j]).
                    double wx = ax[j] - px, wy = ay[j] - py;
                    double t = (wx * ey - wy * ex) / cross;
                    if (t <= 0 || t > limit || t >= best) continue;
                    double s = (wx * dy - wy * dx) / cross;

                    if (s >= -tol && s <= len[j] + tol)
                    {
                        // (a) ray hits the segment itself.
                        best = t; bestKind = FindingKind.ExtendToLine; bestOther = j;
                    }
                    else
                    {
                        // (b) ray hits the extension of j beyond one of its ends; that end must be free.
                        bool beyondEnd = s > len[j];
                        double overrun = beyondEnd ? s - len[j] : -s;
                        int endIndex = 2 * j + (beyondEnd ? 1 : 0);
                        if (overrun <= limit && free[endIndex])
                        {
                            best = t; bestKind = FindingKind.ExtendCorner; bestOther = j;
                        }
                    }
                }
                return bestOther >= 0;
            }

            /// <summary>
            /// Looks inward from a free end (direction D points into the line) for the nearest crossing
            /// with another line. Reports a stub when TouchTol &lt; stub &lt;= ShortLength.
            /// </summary>
            private bool FindOvershoot(int self, double px, double py, double dx, double dy,
                                       out double stub, out int other)
            {
                double shortLen = opt.ShortLength;
                double tol = opt.TouchTol;
                stub = double.PositiveInfinity;
                other = -1;
                if (shortLen <= tol) return false;

                double reach = Math.Min(shortLen, len[self]);
                double r = reach + tol;
                grid.Query(px - r, py - r, px + r, py + r, hits);
                for (int k = 0; k < hits.Count; k++)
                {
                    int j = hits[k];
                    if (j == self || tiny[j]) continue;
                    double ex = ux[j], ey = uy[j];
                    double cross = dx * ey - dy * ex;
                    if (Math.Abs(cross) < ParallelSin) continue;

                    double wx = ax[j] - px, wy = ay[j] - py;
                    double t = (wx * ey - wy * ex) / cross;
                    if (t <= tol || t > reach * (1 + 1e-12) || t >= stub) continue;
                    double s = (wx * dy - wy * dx) / cross;
                    if (s < -tol || s > len[j] + tol) continue;
                    stub = t;
                    other = j;
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

            /// <summary>Collects each segment registered in cells overlapping the box, once.</summary>
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
