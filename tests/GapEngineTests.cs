// GapEngineTests.cs - self-contained unit tests for GapEngine.cs (no test framework, no AutoCAD).
// Build and run with test.bat. Exit code 0 = all tests passed.

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace LineGapChecker.Tests
{
    internal static class Program
    {
        private static int passed, failed;

        // Defaults from the spec, in metres. Most tests draw in metres (1 unit = 1 m).
        private static GapOptions MetreOpts()
        {
            return GapOptions.FromMetres(0.005, 0.10, 1e-6, 1.0);
        }

        private static int Main()
        {
            Run("T-gap is extended", TGap);
            Run("Corner where both lines stop short", CornerBothShort);
            Run("Collinear gap", CollinearGap);
            Run("4 mm overshoot is trimmed", Overshoot4mm);
            Run("4 mm overshoot past a corner is trimmed", OvershootPastCorner);
            Run("20 mm overshoot is ignored", Overshoot20mm);
            Run("Gap larger than max gap is ignored", GapTooLarge);
            Run("Tiny and zero-length lines are trimmed", TinyLines);
            Run("Tiny line counts as touching but is never a target", TinyLineNotTarget);
            Run("Clean corner is not flagged", CleanCorner);
            Run("Clean T is not flagged", CleanT);
            Run("Clean X crossing is not flagged", CleanCross);
            Run("Near-parallel offset line is not an extension target", ParallelOffsetIgnored);
            Run("Nearest hit wins", NearestHitWins);
            Run("Line needing extend and trim is magenta", BothExtendAndTrim);
            Run("Drawing in mm units", MillimetreDrawing);
            Run("INSUNITS conversion table", UnitTable);
            Run("Large coordinates (UTM-like) keep precision", LargeCoordinates);
            Run("Empty input", EmptyInput);
            Run("50,000-line performance run", Performance50k);
            Run("50,000 random long lines (stress + rotation invariance)", RandomStress50k);
            Run("Line stops short of an arc", ArcAsTarget);
            Run("Arc stops short of a line (extends along its circle)", ArcEndStopsShort);
            Run("Arc overshoots a line by 4 mm", ArcOvershoot);
            Run("Gap between two arcs on the same circle", CoCircularGap);
            Run("Arc that almost closes on itself", ArcSelfGap);
            Run("Line stops short of a tangent arc", LineToTangentArc);
            Run("Circle is a target and is never flagged", CircleTarget);
            Run("Closed polyline is clean", ClosedPolylineClean);
            Run("Open polyline ending on its own start is clean", PolylineEndsOnStart);
            Run("Polyline stops short of its own first segment", PolylineSelfGap);
            Run("Polyline overshoots its own first segment", PolylineSelfOvershoot);
            Run("Polyline open corner with both ends short", PolylineOpenCorner);
            Run("Bulge arcs bend the right way", BulgeDirection);
            Run("Polyline ending in an arc stops short", PolylineArcEnd);
            Run("Overshoot across a short last segment", OvershootAcrossVertex);
            Run("Duplicate vertices are ignored", DuplicateVertices);
            Run("Tiny arcs, circles and polylines are trimmed", TinyCurves);
            Run("Polyline in mm units", PolylineMillimetres);
            Run("50,000 mixed lines, arcs and polylines (stress + rotation invariance)", MixedStress50k);

            Console.WriteLine();
            Console.WriteLine("{0} passed, {1} failed", passed, failed);
            return failed == 0 ? 0 : 1;
        }

        // ---------------------------------------------------------------- tests

        private static void TGap()
        {
            // Horizontal base, vertical line stops 50 mm short of it.
            GapResult r = Analyze(MetreOpts(),
                L(0, 0, 10, 0),
                L(5, 0.05, 5, 5));
            ExpectMark(r, 0, LineMark.None);
            ExpectMark(r, 1, LineMark.Extend);
            GapFinding f = Single(r, 1);
            Equal(FindingKind.ExtendToLine, f.Kind, "kind");
            Near(0.05, f.Size, 1e-9, "gap size");
            Near(5, f.X, 1e-9, "x");
            Near(0.05, f.Y, 1e-9, "y");
            Equal(1, r.ExtendCount, "extend count");
            Equal(0, r.TrimCount, "trim count");
        }

        private static void CornerBothShort()
        {
            // Open corner at the origin: horizontal stops 40 mm short, vertical 20 mm short.
            GapResult r = Analyze(MetreOpts(),
                L(0.04, 0, 5, 0),
                L(0, 0.02, 0, 5));
            ExpectMark(r, 0, LineMark.Extend);
            ExpectMark(r, 1, LineMark.Extend);
            GapFinding a = Single(r, 0), b = Single(r, 1);
            Equal(FindingKind.ExtendCorner, a.Kind, "kind a");
            Equal(FindingKind.ExtendCorner, b.Kind, "kind b");
            Near(0.04, a.Size, 1e-9, "gap a");
            Near(0.02, b.Size, 1e-9, "gap b");
        }

        private static void CollinearGap()
        {
            GapResult r = Analyze(MetreOpts(),
                L(0, 0, 5, 0),
                L(5.03, 0, 10, 0));
            ExpectMark(r, 0, LineMark.Extend);
            ExpectMark(r, 1, LineMark.Extend);
            Equal(FindingKind.ExtendCollinear, Single(r, 0).Kind, "kind");
            Near(0.03, Single(r, 0).Size, 1e-9, "gap 0");
            Near(0.03, Single(r, 1).Size, 1e-9, "gap 1");
            Equal(2, r.ExtendCount, "extend count");
        }

        private static void Overshoot4mm()
        {
            GapResult r = Analyze(MetreOpts(),
                L(0, 0, 10, 0),
                L(5, -0.004, 5, 5));
            ExpectMark(r, 0, LineMark.None);
            ExpectMark(r, 1, LineMark.Trim);
            GapFinding f = Single(r, 1);
            Equal(FindingKind.TrimOvershoot, f.Kind, "kind");
            Near(0.004, f.Size, 1e-9, "stub");
            Equal(0, r.ExtendCount, "extend count");
            Equal(1, r.TrimCount, "trim count");
        }

        private static void OvershootPastCorner()
        {
            // The horizontal ends exactly on the vertical; the vertical runs 4 mm past it.
            GapResult r = Analyze(MetreOpts(),
                L(0, 0, 5, 0),
                L(0, -0.004, 0, 5));
            ExpectMark(r, 0, LineMark.None);
            ExpectMark(r, 1, LineMark.Trim);
            Near(0.004, Single(r, 1).Size, 1e-9, "stub");
        }

        private static void Overshoot20mm()
        {
            GapResult r = Analyze(MetreOpts(),
                L(0, 0, 10, 0),
                L(5, -0.02, 5, 5));
            ExpectNoFindings(r);
        }

        private static void GapTooLarge()
        {
            GapResult r = Analyze(MetreOpts(),
                L(0, 0, 10, 0),
                L(5, 0.15, 5, 5),          // T-gap of 150 mm
                L(20, 0, 25, 0),
                L(25.12, 0, 30, 0),        // collinear gap of 120 mm
                L(40.2, 0, 45, 0),
                L(40, 0.2, 40, 5));        // open corner, 200 mm each way
            ExpectNoFindings(r);
        }

        private static void TinyLines()
        {
            GapResult r = Analyze(MetreOpts(),
                L(1, 1, 1.003, 1),         // 3 mm
                L(2, 2, 2, 2),             // zero length
                L(3, 3, 3.005, 3),         // exactly the short length
                L(4, 4, 4.006, 4));        // just above: not tiny, isolated, not flagged
            ExpectMark(r, 0, LineMark.Trim);
            ExpectMark(r, 1, LineMark.Trim);
            ExpectMark(r, 2, LineMark.Trim);
            ExpectMark(r, 3, LineMark.None);
            Equal(FindingKind.TrimShortLine, Single(r, 0).Kind, "kind");
            Near(0.003, Single(r, 0).Size, 1e-9, "length");
            Near(0.0, Single(r, 1).Size, 1e-12, "zero length");
            Equal(3, r.TrimCount, "trim count");
        }

        private static void TinyLineNotTarget()
        {
            // Line 0 ends 50 mm short of a tiny line; the tiny line must not be used as a target.
            GapResult r1 = Analyze(MetreOpts(),
                L(0, 0, 5, 0),
                L(5.05, -0.001, 5.05, 0.001));
            ExpectMark(r1, 0, LineMark.None);
            ExpectMark(r1, 1, LineMark.Trim);

            // Line 0 ends on a tiny line, which in turn sits 50 mm before a wall. Because the
            // tiny line touches line 0's end, that end is not free and is not extended.
            GapResult r2 = Analyze(MetreOpts(),
                L(0, 0, 5, 0),
                L(5, 0, 5.002, 0),
                L(5.05, -5, 5.05, 5));
            ExpectMark(r2, 0, LineMark.None);
            ExpectMark(r2, 1, LineMark.Trim);
            ExpectMark(r2, 2, LineMark.None);
        }

        private static void CleanCorner()
        {
            GapResult r = Analyze(MetreOpts(),
                L(0, 0, 5, 0),
                L(0, 0, 0, 5),
                L(5, 0, 5, 5),
                L(0, 5, 5, 5));            // closed rectangle
            ExpectNoFindings(r);
            Equal(0, r.FreeEndCount, "free ends");
        }

        private static void CleanT()
        {
            GapResult r = Analyze(MetreOpts(),
                L(0, 0, 10, 0),
                L(5, 0, 5, 5));
            ExpectNoFindings(r);
        }

        private static void CleanCross()
        {
            GapResult r = Analyze(MetreOpts(),
                L(0, 0, 10, 10),
                L(0, 10, 10, 0));
            ExpectNoFindings(r);
        }

        private static void ParallelOffsetIgnored()
        {
            // A parallel line 30 mm to the side and just ahead is not a collinear target.
            GapResult r = Analyze(MetreOpts(),
                L(0, 0, 5, 0),
                L(5.02, 0.03, 10, 0.03));
            ExpectNoFindings(r);
        }

        private static void NearestHitWins()
        {
            GapResult r = Analyze(MetreOpts(),
                L(0, 0, 5, 0),
                L(5.08, -1, 5.08, 1),     // 80 mm ahead
                L(5.03, -1, 5.03, 1));    // 30 mm ahead: should win
            GapFinding f = Single(r, 0);
            Near(0.03, f.Size, 1e-9, "nearest gap");
            Equal(2, f.Other, "target line");
        }

        private static void BothExtendAndTrim()
        {
            // Vertical overshoots the bottom line by 4 mm and stops 50 mm short of the top line.
            GapResult r = Analyze(MetreOpts(),
                L(0, 0, 10, 0),
                L(0, 5, 10, 5),
                L(5, -0.004, 5, 4.95));
            ExpectMark(r, 2, LineMark.Trim);
            True(r.NeedsExtend[2], "also needs extend");
            Equal(1, r.BothCount, "both count");
            Equal(1, r.TrimCount, "trim count");
            Equal(0, r.ExtendCount, "blue count excludes magenta lines");
            Equal(2, Of(r, 2).Count, "two findings for the line");
        }

        private static void MillimetreDrawing()
        {
            double upm;
            string name;
            True(GapUnits.TryUnitsPerMetre(4, out upm, out name), "mm supported");
            Near(1000, upm, 1e-12, "mm per metre");
            GapOptions o = GapOptions.FromMetres(0.005, 0.10, 1e-6, upm);
            Near(5, o.ShortLength, 1e-12, "short in mm");
            Near(100, o.MaxGap, 1e-12, "max gap in mm");

            GapResult r = Analyze(o,
                L(0, 0, 10000, 0),
                L(2000, 50, 2000, 5000),   // 50 mm T-gap: extend
                L(4000, 150, 4000, 5000),  // 150 mm: too large
                L(6000, -4, 6000, 5000),   // 4 mm overshoot: trim
                L(8000, -20, 8000, 5000),  // 20 mm overshoot: ignore
                L(9000, 1000, 9003, 1000));// 3 mm line: trim
            ExpectMark(r, 0, LineMark.None);
            ExpectMark(r, 1, LineMark.Extend);
            ExpectMark(r, 2, LineMark.None);
            ExpectMark(r, 3, LineMark.Trim);
            ExpectMark(r, 4, LineMark.None);
            ExpectMark(r, 5, LineMark.Trim);
            Near(50, Single(r, 1).Size, 1e-9, "gap in drawing units");
            Near(0.05, Single(r, 1).Size / upm, 1e-12, "gap in metres");
        }

        private static void UnitTable()
        {
            CheckUnit(1, 39.37007874015748);   // inches
            CheckUnit(2, 3.280839895013123);   // feet
            CheckUnit(3, 1.0 / 1609.344);      // miles
            CheckUnit(4, 1000);                // mm
            CheckUnit(5, 100);                 // cm
            CheckUnit(6, 1);                   // m
            CheckUnit(7, 0.001);               // km
            CheckUnit(10, 1.0936132983377078); // yards
            CheckUnit(14, 10);                 // dm
            double upm;
            string name;
            True(!GapUnits.TryUnitsPerMetre(0, out upm, out name), "undefined is reported");
            Near(1, upm, 0, "undefined falls back to 1 unit = 1 m");
        }

        private static void LargeCoordinates()
        {
            // Same T-gap as above, shifted to UTM-like coordinates in millimetres.
            double x0 = 500000000, y0 = 3000000000;
            GapOptions o = GapOptions.FromMetres(0.005, 0.10, 1e-6, 1000);
            GapResult r = Analyze(o,
                L(x0, y0, x0 + 10000, y0),
                L(x0 + 5000, y0 + 50, x0 + 5000, y0 + 5000),
                L(x0 + 7000, y0 - 4, x0 + 7000, y0 + 5000));
            ExpectMark(r, 1, LineMark.Extend);
            ExpectMark(r, 2, LineMark.Trim);
            Near(50, Single(r, 1).Size, 1e-6, "gap");
            Near(x0 + 5000, Single(r, 1).X, 1e-6, "x in original coordinates");
        }

        private static void EmptyInput()
        {
            GapResult r = GapEngine.Analyze(new Seg2[0], MetreOpts());
            Equal(0, r.LineCount, "line count");
            Equal(0, r.Findings.Count, "findings");
        }

        private static void Performance50k()
        {
            // A 158 x 158 lattice of 1 m cells = 50,244 lines, all clean, then known defects.
            const int N = 158;
            List<Seg2> segs = new List<Seg2>();
            int expectExtend = 0, expectTrim = 0;
            for (int j = 0; j <= N; j++)
            {
                for (int i = 0; i < N; i++)
                {
                    // Horizontal (i,j)-(i+1,j). Some are 30 mm short at their right end.
                    bool shortEnd = (i + j * 7) % 97 == 0 && i < N - 1;
                    segs.Add(new Seg2(i, j, shortEnd ? i + 1 - 0.03 : i + 1, j));
                    if (shortEnd) expectExtend++;
                }
            }
            for (int i = 0; i <= N; i++)
            {
                for (int j = 0; j < N; j++)
                {
                    // Vertical (i,j)-(i,j+1). On the bottom row some run 4 mm below y = 0.
                    bool overshoot = j == 0 && i % 10 == 3;
                    segs.Add(new Seg2(i, overshoot ? -0.004 : j, i, j + 1));
                    if (overshoot) expectTrim++;
                }
            }
            Seg2[] lines = segs.ToArray();
            True(lines.Length >= 50000, "at least 50,000 lines");

            Stopwatch sw = Stopwatch.StartNew();
            GapResult r = GapEngine.Analyze(lines, MetreOpts());
            sw.Stop();

            Console.WriteLine("      {0:N0} lines analysed in {1} ms ({2} extend, {3} trim)",
                lines.Length, sw.ElapsedMilliseconds, r.ExtendCount, r.TrimCount);
            Equal(expectExtend, r.ExtendCount, "extend count");
            Equal(expectTrim, r.TrimCount, "trim count");
            Equal(expectExtend + expectTrim, r.Findings.Count, "finding count");
            True(sw.Elapsed.TotalSeconds < 5.0, "finished in under 5 s");
        }

        private static void RandomStress50k()
        {
            // Random lines 0.5-80 m long in any direction inside a 2 km square: lots of long
            // diagonals and crossings, the worst case for a grid index.
            Random rnd = new Random(12345);
            Seg2[] a = new Seg2[50000];
            Seg2[] b = new Seg2[a.Length];
            for (int i = 0; i < a.Length; i++)
            {
                double x = rnd.NextDouble() * 2000, y = rnd.NextDouble() * 2000;
                double ang = rnd.NextDouble() * Math.PI * 2, len = 0.5 + rnd.NextDouble() * 79.5;
                a[i] = new Seg2(x, y, x + Math.Cos(ang) * len, y + Math.Sin(ang) * len);
                // Same drawing rotated 90 degrees: exact in floating point, but every grid cell changes.
                b[i] = new Seg2(-a[i].Y1, a[i].X1, -a[i].Y2, a[i].X2);
            }

            Stopwatch sw = Stopwatch.StartNew();
            GapResult ra = GapEngine.Analyze(a, MetreOpts());
            sw.Stop();
            GapResult rb = GapEngine.Analyze(b, MetreOpts());
            Console.WriteLine("      {0:N0} random lines analysed in {1} ms ({2} extend, {3} trim)",
                a.Length, sw.ElapsedMilliseconds, ra.ExtendCount, ra.TrimCount);

            True(sw.Elapsed.TotalSeconds < 5.0, "finished in under 5 s");
            Equal(ra.Findings.Count, rb.Findings.Count, "same findings after rotation");
            for (int i = 0; i < a.Length; i++)
                if (ra.MarkOf(i) != rb.MarkOf(i)) throw new TestFailure("mark differs for line " + i);
        }

        // ---------------------------------------------------------------- arcs, circles, polylines

        private static void ArcAsTarget()
        {
            // Upper half of a 5 m circle; a vertical line stops 50 mm above its top.
            GapResult r = AnalyzeC(MetreOpts(),
                GapCurve.FromArc(0, 0, 5, 0, Math.PI),
                Line(0, 5.05, 0, 8));
            ExpectMark(r, 0, LineMark.None);
            ExpectMark(r, 1, LineMark.Extend);
            GapFinding f = Single(r, 1);
            Equal(FindingKind.ExtendToLine, f.Kind, "kind");
            Near(0.05, f.Size, 1e-9, "gap");
            Equal(0, f.Other, "target is the arc");
        }

        private static void ArcEndStopsShort()
        {
            // A quarter arc of radius 1 m stops 30 mm (measured along the arc) before the wall x = 0.
            GapResult r = AnalyzeC(MetreOpts(),
                GapCurve.FromArc(0, 0, 1, 0, Math.PI / 2 - 0.03),
                Line(0, 0.5, 0, 2));
            ExpectMark(r, 0, LineMark.Extend);
            ExpectMark(r, 1, LineMark.None);
            GapFinding f = Single(r, 0);
            Equal(FindingKind.ExtendToLine, f.Kind, "kind");
            Near(0.03, f.Size, 1e-9, "gap along the arc");
            Near(Math.Cos(Math.PI / 2 - 0.03), f.X, 1e-9, "x of the free end");
        }

        private static void ArcOvershoot()
        {
            // The arc starts 4 mm (along the arc) below the line y = 0 that it crosses.
            GapResult r = AnalyzeC(MetreOpts(),
                GapCurve.FromArc(0, 0, 1, -0.004, Math.PI / 2 + 0.004),
                Line(-2, 0, 2, 0));
            ExpectMark(r, 0, LineMark.Trim);
            ExpectMark(r, 1, LineMark.None);
            GapFinding f = Single(r, 0);
            Equal(FindingKind.TrimOvershoot, f.Kind, "kind");
            Near(0.004, f.Size, 1e-9, "stub along the arc");
        }

        private static void CoCircularGap()
        {
            // One 2 m arc split in two with a 30 mm gap.
            GapResult r = AnalyzeC(MetreOpts(),
                GapCurve.FromArc(0, 0, 2, 0, Math.PI / 2),
                GapCurve.FromArc(0, 0, 2, Math.PI / 2 + 0.015, Math.PI / 2 - 0.015));
            ExpectMark(r, 0, LineMark.Extend);
            ExpectMark(r, 1, LineMark.Extend);
            Equal(FindingKind.ExtendCollinear, Single(r, 0).Kind, "kind");
            Near(0.03, Single(r, 0).Size, 1e-9, "gap 0");
            Near(0.03, Single(r, 1).Size, 1e-9, "gap 1");
        }

        private static void ArcSelfGap()
        {
            // A circle drawn as an arc that leaves a 30 mm gap.
            GapResult r = AnalyzeC(MetreOpts(), GapCurve.FromArc(3, 4, 1, 1, 2 * Math.PI - 0.03));
            ExpectMark(r, 0, LineMark.Extend);
            Equal(2, Of(r, 0).Count, "one finding per end");
            foreach (GapFinding f in Of(r, 0))
            {
                Equal(FindingKind.ExtendCollinear, f.Kind, "kind");
                Near(0.03, f.Size, 1e-9, "gap");
                Equal(0, f.Other, "target is itself");
            }
        }

        private static void LineToTangentArc()
        {
            // A line runs along y = 1 and stops 30 mm before an arc that starts tangent to it.
            GapResult r = AnalyzeC(MetreOpts(),
                Line(-5, 1, -0.03, 1),
                GapCurve.FromArc(0, 0, 1, Math.PI / 2, -Math.PI / 2));
            ExpectMark(r, 0, LineMark.Extend);
            ExpectMark(r, 1, LineMark.None);
            Near(0.03, Single(r, 0).Size, 1e-9, "gap");
        }

        private static void CircleTarget()
        {
            GapResult r = AnalyzeC(MetreOpts(),
                GapCurve.FromCircle(0, 0, 1),
                Line(3, 0, 1.04, 0),          // stops 40 mm short of the circle
                Line(-3, 0, -1, 0),           // ends exactly on it
                Line(0.5, -3, 0.5, 3));       // passes through it
            ExpectMark(r, 0, LineMark.None);
            ExpectMark(r, 1, LineMark.Extend);
            ExpectMark(r, 2, LineMark.None);
            ExpectMark(r, 3, LineMark.None);
            Near(0.04, Single(r, 1).Size, 1e-9, "gap");
            Equal(1, r.Findings.Count, "findings");
        }

        private static void ClosedPolylineClean()
        {
            GapResult r = AnalyzeC(MetreOpts(), Poly(true, 0, 0, 5, 0, 5, 5, 0, 5));
            ExpectNoFindings(r);
            Equal(0, r.FreeEndCount, "free ends");
        }

        private static void PolylineEndsOnStart()
        {
            // Open polyline whose last vertex repeats the first one.
            GapResult r = AnalyzeC(MetreOpts(), Poly(false, 0, 0, 5, 0, 5, 5, 0, 5, 0, 0));
            ExpectNoFindings(r);
            Equal(0, r.FreeEndCount, "free ends");
        }

        private static void PolylineSelfGap()
        {
            // The last segment stops 50 mm short of the polyline's own first segment.
            GapResult r = AnalyzeC(MetreOpts(), Poly(false, 0, 0, 5, 0, 5, 5, 0, 5, 0, 0.05));
            ExpectMark(r, 0, LineMark.Extend);
            GapFinding f = Single(r, 0);
            Equal(FindingKind.ExtendToLine, f.Kind, "kind");
            Near(0.05, f.Size, 1e-9, "gap");
            Equal(0, f.Other, "target is itself");
            Near(0.05, f.Y, 1e-12, "at the end vertex");
        }

        private static void PolylineSelfOvershoot()
        {
            // The last segment runs 4 mm past the polyline's own start.
            GapResult r = AnalyzeC(MetreOpts(), Poly(false, 0, 0, 5, 0, 5, 5, 0, 5, 0, -0.004));
            ExpectMark(r, 0, LineMark.Trim);
            GapFinding f = Single(r, 0);
            Equal(FindingKind.TrimOvershoot, f.Kind, "kind");
            Near(0.004, f.Size, 1e-9, "stub");
        }

        private static void PolylineOpenCorner()
        {
            // A rectangle whose two ends stop 40 mm and 20 mm short of the shared corner.
            GapResult r = AnalyzeC(MetreOpts(), Poly(false, 0.04, 0, 5, 0, 5, 5, 0, 5, 0, 0.02));
            ExpectMark(r, 0, LineMark.Extend);
            List<GapFinding> fs = Of(r, 0);
            Equal(2, fs.Count, "one finding per end");
            foreach (GapFinding f in fs) Equal(FindingKind.ExtendCorner, f.Kind, "kind");
        }

        private static void BulgeDirection()
        {
            // Bulge +1 from (0,0) to (2,0) is a counter-clockwise half circle, so it dips to (1,-1).
            GapResult down = AnalyzeC(MetreOpts(),
                GapCurve.FromPolyline(new double[] { 0, 2 }, new double[] { 0, 0 }, new double[] { 1, 0 }, false),
                Line(1, -2, 1, -1.05));
            ExpectMark(down, 1, LineMark.Extend);
            Near(0.05, Single(down, 1).Size, 1e-9, "gap below");

            // Bulge -1 is clockwise and rises to (1,1).
            GapResult up = AnalyzeC(MetreOpts(),
                GapCurve.FromPolyline(new double[] { 0, 2 }, new double[] { 0, 0 }, new double[] { -1, 0 }, false),
                Line(1, 2, 1, 1.05));
            ExpectMark(up, 1, LineMark.Extend);
            Near(0.05, Single(up, 1).Size, 1e-9, "gap above");
            ExpectMark(up, 0, LineMark.None);
        }

        private static void PolylineArcEnd()
        {
            // Straight run, then a clockwise arc (centre 0,0, radius 1) that stops 30 mm along
            // the arc before the wall y = 0.
            double end = 0.03;
            double sweep = -(Math.PI / 2 - end);
            GapResult r = AnalyzeC(MetreOpts(),
                GapCurve.FromPolyline(
                    new double[] { -3, 0, Math.Cos(end) },
                    new double[] { 1, 1, Math.Sin(end) },
                    new double[] { 0, Math.Tan(sweep / 4), 0 }, false),
                Line(0.5, 0, 2, 0));
            ExpectMark(r, 0, LineMark.Extend);
            ExpectMark(r, 1, LineMark.None);
            GapFinding f = Single(r, 0);
            Equal(FindingKind.ExtendToLine, f.Kind, "kind");
            Near(0.03, f.Size, 1e-9, "gap along the arc");
        }

        private static void OvershootAcrossVertex()
        {
            // The polyline crosses the wall x = 5, turns 1 mm later and ends 3 mm after that:
            // the stub (4 mm in total) runs across a vertex.
            GapResult r = AnalyzeC(MetreOpts(),
                Poly(false, 0, 0, 5.001, 0, 5.001, 0.003),
                Line(5, -1, 5, 1));
            ExpectMark(r, 0, LineMark.Trim);
            ExpectMark(r, 1, LineMark.None);
            GapFinding f = Single(r, 0);
            Equal(FindingKind.TrimOvershoot, f.Kind, "kind");
            Near(0.004, f.Size, 1e-9, "stub");
        }

        private static void DuplicateVertices()
        {
            // Repeated last vertex: the end still points along the real last segment.
            GapResult r = AnalyzeC(MetreOpts(),
                Poly(false, 0, 0, 5, 0, 5, 0),
                Line(5.05, -1, 5.05, 1));
            ExpectMark(r, 0, LineMark.Extend);
            Near(0.05, Single(r, 0).Size, 1e-9, "gap");
        }

        private static void TinyCurves()
        {
            GapResult r = AnalyzeC(MetreOpts(),
                GapCurve.FromArc(0, 0, 1, 0, 0.003),                 // 3 mm arc
                GapCurve.FromCircle(10, 10, 0.0005),                 // 3.1 mm round
                Poly(false, 20, 20, 20.002, 20, 20.002, 20.002),     // 4 mm polyline
                GapCurve.FromArc(30, 30, 1, 0, 0.006));              // 6 mm arc: fine
            ExpectMark(r, 0, LineMark.Trim);
            ExpectMark(r, 1, LineMark.Trim);
            ExpectMark(r, 2, LineMark.Trim);
            ExpectMark(r, 3, LineMark.None);
            Near(0.003, Single(r, 0).Size, 1e-12, "arc length");
            Near(0.004, Single(r, 2).Size, 1e-12, "polyline length");
        }

        private static void PolylineMillimetres()
        {
            GapOptions o = GapOptions.FromMetres(0.005, 0.10, 1e-6, 1000);
            GapResult r = AnalyzeC(o,
                Poly(true, 0, 0, 10000, 0, 10000, 5000, 0, 5000),   // room outline, closed
                Poly(false, 2000, 50, 2000, 2000, 3000, 2000),     // 50 mm short of the wall: extend
                Poly(false, 6000, -4, 6000, 2000, 7000, 2000),     // 4 mm through the wall: trim
                Poly(false, 8000, 150, 8000, 2000));               // 150 mm: too large
            ExpectMark(r, 0, LineMark.None);
            ExpectMark(r, 1, LineMark.Extend);
            ExpectMark(r, 2, LineMark.Trim);
            ExpectMark(r, 3, LineMark.None);
            Near(50, Single(r, 1).Size, 1e-9, "gap in mm");
            Near(4, Single(r, 2).Size, 1e-9, "stub in mm");
        }

        private static void MixedStress50k()
        {
            // Random lines, bulge arcs and polylines with arc segments in a 2 km square. The same
            // drawing rotated 90 degrees (exact for vertices) must give the same marks.
            Random rnd = new Random(777);
            int count = 50000;
            GapCurve[] a = new GapCurve[count], b = new GapCurve[count];
            for (int i = 0; i < count; i++)
            {
                int nv = i % 3 == 0 ? 2 : 2 + rnd.Next(5);
                double[] xs = new double[nv], ys = new double[nv], bs = new double[nv];
                double x = rnd.NextDouble() * 2000, y = rnd.NextDouble() * 2000;
                for (int v = 0; v < nv; v++)
                {
                    xs[v] = x; ys[v] = y;
                    double ang = rnd.NextDouble() * Math.PI * 2, step = 0.5 + rnd.NextDouble() * 20;
                    x += Math.Cos(ang) * step; y += Math.Sin(ang) * step;
                    bs[v] = i % 3 == 1 ? 0 : (rnd.NextDouble() < 0.5 ? 0 : rnd.NextDouble() * 2 - 1);
                }
                bool isClosed = i % 17 == 0 && nv > 2;
                a[i] = GapCurve.FromPolyline(xs, ys, bs, isClosed);
                double[] rx = new double[nv], ry = new double[nv];
                for (int v = 0; v < nv; v++) { rx[v] = -ys[v]; ry[v] = xs[v]; }
                b[i] = GapCurve.FromPolyline(rx, ry, bs, isClosed);
            }

            Stopwatch sw = Stopwatch.StartNew();
            GapResult ra = GapEngine.Analyze(a, MetreOpts());
            sw.Stop();
            GapResult rb = GapEngine.Analyze(b, MetreOpts());
            Console.WriteLine("      {0:N0} mixed objects analysed in {1} ms ({2} extend, {3} trim)",
                count, sw.ElapsedMilliseconds, ra.ExtendCount, ra.TrimCount);

            True(sw.Elapsed.TotalSeconds < 5.0, "finished in under 5 s");
            Equal(ra.Findings.Count, rb.Findings.Count, "same findings after rotation");
            for (int i = 0; i < count; i++)
                if (ra.MarkOf(i) != rb.MarkOf(i)) throw new TestFailure("mark differs for object " + i);
        }

        // ---------------------------------------------------------------- helpers

        private static Seg2 L(double x1, double y1, double x2, double y2)
        {
            return new Seg2(x1, y1, x2, y2);
        }

        private static GapResult Analyze(GapOptions o, params Seg2[] lines)
        {
            return GapEngine.Analyze(lines, o);
        }

        private static GapResult AnalyzeC(GapOptions o, params GapCurve[] curves)
        {
            return GapEngine.Analyze(curves, o);
        }

        private static GapCurve Line(double x1, double y1, double x2, double y2)
        {
            return GapCurve.FromLine(x1, y1, x2, y2);
        }

        /// <summary>Straight-segment polyline from x,y pairs.</summary>
        private static GapCurve Poly(bool isClosed, params double[] xy)
        {
            double[] xs = new double[xy.Length / 2], ys = new double[xy.Length / 2];
            for (int i = 0; i < xs.Length; i++) { xs[i] = xy[2 * i]; ys[i] = xy[2 * i + 1]; }
            return GapCurve.FromPolyline(xs, ys, null, isClosed);
        }

        private static List<GapFinding> Of(GapResult r, int line)
        {
            List<GapFinding> list = new List<GapFinding>();
            foreach (GapFinding f in r.Findings) if (f.Line == line) list.Add(f);
            return list;
        }

        private static GapFinding Single(GapResult r, int line)
        {
            List<GapFinding> list = Of(r, line);
            if (list.Count != 1)
                throw new TestFailure("expected exactly 1 finding for line " + line + ", got " + list.Count);
            return list[0];
        }

        private static void ExpectMark(GapResult r, int line, LineMark mark)
        {
            if (r.MarkOf(line) != mark)
                throw new TestFailure("line " + line + ": expected " + mark + ", got " + r.MarkOf(line));
        }

        private static void ExpectNoFindings(GapResult r)
        {
            if (r.Findings.Count != 0)
            {
                GapFinding f = r.Findings[0];
                throw new TestFailure("expected no findings, got " + r.Findings.Count +
                    " (first: line " + f.Line + " " + f.Kind + " size " + f.Size + ")");
            }
        }

        private static void CheckUnit(int code, double expected)
        {
            double upm;
            string name;
            True(GapUnits.TryUnitsPerMetre(code, out upm, out name), "INSUNITS " + code + " supported");
            Near(expected, upm, Math.Abs(expected) * 1e-12, "INSUNITS " + code + " (" + name + ")");
        }

        private static void Equal<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new TestFailure(what + ": expected " + expected + ", got " + actual);
        }

        private static void Near(double expected, double actual, double tol, string what)
        {
            if (Math.Abs(expected - actual) > tol)
                throw new TestFailure(what + ": expected " + expected + ", got " + actual);
        }

        private static void True(bool condition, string what)
        {
            if (!condition) throw new TestFailure(what);
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                passed++;
                Console.WriteLine("PASS  " + name);
            }
            catch (System.Exception ex)
            {
                failed++;
                Console.WriteLine("FAIL  " + name + ": " + ex.Message);
            }
        }

        private sealed class TestFailure : System.Exception
        {
            public TestFailure(string message) : base(message) { }
        }
    }
}
