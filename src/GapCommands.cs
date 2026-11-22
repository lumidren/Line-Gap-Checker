// GapCommands.cs - AutoCAD command layer for LineGapChecker.
//
// Commands: GAPCHECK, GAPSET, GAPRESET. All geometry work is in GapEngine.cs.
// Checks LINE, ARC, CIRCLE, LWPOLYLINE and simple 2D/3D POLYLINE objects.
// Written in C# 5 so it builds with the .NET Framework csc.exe (AutoCAD 2024 and older)
// as well as with the net8.0-windows SDK project (AutoCAD 2025 and newer).
//
// Note: Autodesk.AutoCAD.Runtime also defines a type called Exception, so every catch
// below names System.Exception or Autodesk.AutoCAD.Runtime.Exception explicitly.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(LineGapChecker.GapPlugin))]
[assembly: CommandClass(typeof(LineGapChecker.GapCommands))]

namespace LineGapChecker
{
    /// <summary>Runs once when the DLL is NETLOADed.</summary>
    public sealed class GapPlugin : IExtensionApplication
    {
        public void Initialize()
        {
            try
            {
                Document doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc != null) GapCommands.Print(doc.Editor, GapCommands.HelpText);
            }
            catch (System.Exception)
            {
                // Never let a message failure stop the plugin from loading.
            }
        }

        public void Terminate()
        {
        }
    }

    /// <summary>Thresholds kept for the AutoCAD session (not saved in the drawing).</summary>
    internal static class GapSettings
    {
        public const double DefaultShortM = 0.005;
        public const double DefaultMaxGapM = 0.10;
        public const double DefaultTouchTolM = 1e-6;

        public static double ShortLengthM = DefaultShortM;
        public static double MaxGapM = DefaultMaxGapM;
        public static double TouchTolM = DefaultTouchTolM;
        /// <summary>Drawing units per metre. 0 = automatic from INSUNITS.</summary>
        public static double UnitsPerMetre = 0;

        public static void Reset()
        {
            ShortLengthM = DefaultShortM;
            MaxGapM = DefaultMaxGapM;
            TouchTolM = DefaultTouchTolM;
            UnitsPerMetre = 0;
        }
    }

    public sealed class GapCommands
    {
        internal const string AppName = "LINEGAPCHECK";
        private const short XDataVersion = 1;
        private const short BlueAci = 5;
        private const short MagentaAci = 6;
        private const int MaxListed = 30;

        internal const string HelpText =
            "\nLineGapChecker loaded. Commands:" +
            "\n  GAPCHECK  Check lines, arcs and polylines: blue = needs extending, magenta = needs trimming" +
            "\n  GAPSET    Set thresholds for this session" +
            "\n  GAPRESET  Restore the original colours of objects marked by GAPCHECK\n";

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ------------------------------------------------------------------ GAPCHECK

        [CommandMethod("GAPCHECK", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void GapCheck()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            try
            {
                RunCheck(doc.Database, ed);
            }
            catch (System.Exception ex)
            {
                Print(ed, "\nGAPCHECK failed: " + ex.Message + "\n");
            }
        }

        private static void RunCheck(Database db, Editor ed)
        {
            string unitText;
            double upm = ResolveUnitsPerMetre(db, ed, out unitText);

            // Selection happens before the transaction; Enter (empty selection) means "all".
            bool selectAll;
            ObjectId[] picked;
            if (!PromptForLines(ed, out picked, out selectAll)) return;

            Stopwatch sw = Stopwatch.StartNew();
            GapResult result;
            ObjectCounts counts = new ObjectCounts();
            int restored, restoreSkipped, lockedSkipped = 0;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                LayerLockCache locks = new LayerLockCache(tr);

                // 1. Undo the previous run so results never stack.
                RestoreMarked(tr, db, locks, out restored, out restoreSkipped);

                // 2. Read the geometry (objects stay open for read only).
                ObjectId[] ids = selectAll ? AllCandidatesInCurrentSpace(tr, db) : picked;
                List<Entity> entities = new List<Entity>(ids.Length);
                List<GapCurve> curves = new List<GapCurve>(ids.Length);
                for (int i = 0; i < ids.Length; i++)
                {
                    Entity ent = tr.GetObject(ids[i], OpenMode.ForRead) as Entity;
                    if (ent == null) continue;
                    GapCurve curve = ToCurve(tr, ent, counts);
                    if (curve == null) continue;
                    entities.Add(ent);
                    curves.Add(curve);
                }

                // 3. Analyse.
                GapOptions opt = GapOptions.FromMetres(
                    GapSettings.ShortLengthM, GapSettings.MaxGapM, GapSettings.TouchTolM, upm);
                result = GapEngine.Analyze(curves.ToArray(), opt);

                // 4. Colour. Upgrade to write only for objects that change.
                if (result.ExtendCount + result.TrimCount > 0) EnsureRegApp(tr, db);
                for (int i = 0; i < entities.Count; i++)
                {
                    LineMark mark = result.MarkOf(i);
                    if (mark == LineMark.None) continue;
                    Entity ent = entities[i];
                    if (locks.IsLocked(ent.LayerId) || !TryUpgradeOpen(ent))
                    {
                        lockedSkipped++;
                        continue;
                    }
                    // An object still carrying our XData (it was on a locked layer during the
                    // restore) already holds its true original colour: keep that one.
                    if (!HasMarkXData(ent)) SaveOriginalColor(ent);
                    ent.ColorIndex = mark == LineMark.Trim ? MagentaAci : BlueAci;
                }

                tr.Commit();
            }
            sw.Stop();

            PrintSummary(ed, result, upm, unitText, counts,
                         restored, restoreSkipped, lockedSkipped, sw.ElapsedMilliseconds);
        }

        /// <summary>Object types GAPCHECK reads, as DXF names (also the selection filter).</summary>
        private const string CandidateDxfNames = "LINE,ARC,CIRCLE,LWPOLYLINE,POLYLINE";

        private sealed class ObjectCounts
        {
            public int Lines, Arcs, Circles, Polylines, Skipped;

            public int Checked
            {
                get { return Lines + Arcs + Circles + Polylines; }
            }
        }

        /// <summary>Returns false if the user cancelled.</summary>
        private static bool PromptForLines(Editor ed, out ObjectId[] ids, out bool selectAll)
        {
            ids = null;
            selectAll = false;

            TypedValue[] filterList = { new TypedValue((int)DxfCode.Start, CandidateDxfNames) };
            SelectionFilter filter = new SelectionFilter(filterList);
            PromptSelectionOptions pso = new PromptSelectionOptions();
            pso.MessageForAdding = "\nSelect lines, arcs, circles or polylines to check [Enter = all]: ";
            pso.RejectObjectsFromNonCurrentSpace = true;

            PromptSelectionResult psr = ed.GetSelection(pso, filter);
            switch (psr.Status)
            {
                case PromptStatus.OK:
                    ids = psr.Value.GetObjectIds();
                    return true;
                case PromptStatus.Cancel:
                    return false;
                default:
                    // Enter on an empty selection comes back as Error (or None): use everything.
                    selectAll = true;
                    return true;
            }
        }

        private static ObjectId[] AllCandidatesInCurrentSpace(Transaction tr, Database db)
        {
            List<ObjectId> ids = new List<ObjectId>();
            BlockTableRecord space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
            foreach (ObjectId id in space)
            {
                if (IsCandidate(id.ObjectClass)) ids.Add(id);
            }
            return ids.ToArray();
        }

        private static bool IsCandidate(RXClass cls)
        {
            switch (cls.DxfName)
            {
                case "LINE":
                case "ARC":
                case "CIRCLE":
                case "LWPOLYLINE":
                case "POLYLINE":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Converts a supported object to engine geometry in WCS (Z is ignored), or returns null
        /// and counts it as skipped: spline- or curve-fit polylines, meshes, and arcs, circles or
        /// polylines that are not drawn flat in the XY plane.
        /// </summary>
        private static GapCurve ToCurve(Transaction tr, Entity ent, ObjectCounts counts)
        {
            Line ln = ent as Line;
            if (ln != null)
            {
                Point3d a = ln.StartPoint, b = ln.EndPoint;
                counts.Lines++;
                return GapCurve.FromLine(a.X, a.Y, b.X, b.Y);
            }

            Arc arc = ent as Arc;
            if (arc != null)
            {
                double nz = arc.Normal.Z;
                if (!IsFlat(nz)) { counts.Skipped++; return null; }
                Point3d c = arc.Center, s = arc.StartPoint;
                // StartPoint is in WCS. Seen from +Z, an arc whose normal points down runs clockwise.
                double startAngle = Math.Atan2(s.Y - c.Y, s.X - c.X);
                double sweep = nz > 0 ? arc.TotalAngle : -arc.TotalAngle;
                counts.Arcs++;
                return GapCurve.FromArc(c.X, c.Y, arc.Radius, startAngle, sweep);
            }

            Circle circle = ent as Circle;
            if (circle != null)
            {
                if (!IsFlat(circle.Normal.Z)) { counts.Skipped++; return null; }
                Point3d c = circle.Center;
                counts.Circles++;
                return GapCurve.FromCircle(c.X, c.Y, circle.Radius);
            }

            Polyline pl = ent as Polyline;
            if (pl != null)
            {
                double nz = pl.Normal.Z;
                if (!IsFlat(nz)) { counts.Skipped++; return null; }
                int nv = pl.NumberOfVertices;
                if (nv == 0) { counts.Skipped++; return null; }
                double[] x = new double[nv], y = new double[nv], bulge = new double[nv];
                for (int i = 0; i < nv; i++)
                {
                    Point3d p = pl.GetPoint3dAt(i);     // WCS
                    x[i] = p.X;
                    y[i] = p.Y;
                    // Bulges are defined around the polyline's normal: flip them for a downward normal.
                    bulge[i] = nz > 0 ? pl.GetBulgeAt(i) : -pl.GetBulgeAt(i);
                }
                counts.Polylines++;
                return GapCurve.FromPolyline(x, y, bulge, pl.Closed);
            }

            Polyline2d p2 = ent as Polyline2d;
            if (p2 != null)
            {
                double nz = p2.Normal.Z;
                if (p2.PolyType != Poly2dType.SimplePoly || !IsFlat(nz)) { counts.Skipped++; return null; }
                List<double> x = new List<double>(), y = new List<double>(), bulge = new List<double>();
                foreach (ObjectId vid in p2)
                {
                    Vertex2d v = tr.GetObject(vid, OpenMode.ForRead) as Vertex2d;
                    if (v == null) continue;
                    Point3d p = p2.VertexPosition(v);   // WCS
                    x.Add(p.X);
                    y.Add(p.Y);
                    bulge.Add(nz > 0 ? v.Bulge : -v.Bulge);
                }
                if (x.Count == 0) { counts.Skipped++; return null; }
                counts.Polylines++;
                return GapCurve.FromPolyline(x.ToArray(), y.ToArray(), bulge.ToArray(), p2.Closed);
            }

            Polyline3d p3 = ent as Polyline3d;
            if (p3 != null)
            {
                if (p3.PolyType != Poly3dType.SimplePoly) { counts.Skipped++; return null; }
                List<double> x = new List<double>(), y = new List<double>();
                foreach (ObjectId vid in p3)
                {
                    PolylineVertex3d v = tr.GetObject(vid, OpenMode.ForRead) as PolylineVertex3d;
                    if (v == null) continue;
                    x.Add(v.Position.X);                // WCS; checked in plan view like LINEs
                    y.Add(v.Position.Y);
                }
                if (x.Count == 0) { counts.Skipped++; return null; }
                counts.Polylines++;
                return GapCurve.FromPolyline(x.ToArray(), y.ToArray(), null, p3.Closed);
            }

            counts.Skipped++;   // polyface and polygon meshes also have the DXF name POLYLINE
            return null;
        }

        /// <summary>True when an object's normal is (anti)parallel to the Z axis.</summary>
        private static bool IsFlat(double normalZ)
        {
            // 1e-6 allows a tilt of about 0.08 degrees: the plan view then shrinks by at most 1 ppm.
            return Math.Abs(normalZ) >= 1 - 1e-6;
        }

        private static double ResolveUnitsPerMetre(Database db, Editor ed, out string unitText)
        {
            if (GapSettings.UnitsPerMetre > 0)
            {
                unitText = Num(GapSettings.UnitsPerMetre) + " drawing units per metre (set with GAPSET)";
                return GapSettings.UnitsPerMetre;
            }

            int code = (int)db.Insunits;
            double upm;
            string name;
            if (GapUnits.TryUnitsPerMetre(code, out upm, out name))
            {
                unitText = name + " (INSUNITS=" + code + ")";
                return upm;
            }

            Print(ed, "\nWarning: INSUNITS is " + name + ". Assuming 1 drawing unit = 1 m." +
                      "\n         Set INSUNITS, or set the drawing units per metre with GAPSET.");
            unitText = "assumed metres (INSUNITS=" + code + ")";
            return 1.0;
        }

        private static void PrintSummary(Editor ed, GapResult r, double upm, string unitText,
                                         ObjectCounts counts, int restored,
                                         int restoreSkipped, int lockedSkipped, long ms)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("\n\nGAPCHECK: ").Append(counts.Checked).Append(" object(s) checked in ")
              .Append(ms).Append(" ms. Units: ").Append(unitText).Append('.');
            if (counts.Checked > 0)
                sb.Append("\n  ").Append(counts.Lines).Append(" line(s), ")
                  .Append(counts.Arcs).Append(" arc(s), ")
                  .Append(counts.Circles).Append(" circle(s), ")
                  .Append(counts.Polylines).Append(" polyline(s).");
            sb.Append("\n  Thresholds: short/overshoot ").Append(Num(GapSettings.ShortLengthM))
              .Append(" m, max gap ").Append(Num(GapSettings.MaxGapM))
              .Append(" m, touch tolerance ").Append(Num(GapSettings.TouchTolM)).Append(" m.");
            sb.Append("\n  To extend (blue):   ").Append(r.ExtendCount);
            sb.Append("\n  To trim (magenta):  ").Append(r.TrimCount);
            if (r.BothCount > 0)
                sb.Append("   (").Append(r.BothCount)
                  .Append(" of these also need extending; they are coloured magenta)");

            if (restored > 0)
                sb.Append("\n  ").Append(restored).Append(" object(s) from the previous run were restored first.");
            if (restoreSkipped > 0)
                sb.Append("\n  ").Append(restoreSkipped)
                  .Append(" marked object(s) on locked layers could not be restored.");
            if (lockedSkipped > 0)
                sb.Append("\n  ").Append(lockedSkipped)
                  .Append(" flagged object(s) are on locked layers and were not recoloured. Unlock and run GAPCHECK again.");
            if (counts.Skipped > 0)
                sb.Append("\n  ").Append(counts.Skipped)
                  .Append(" object(s) were skipped: spline- or curve-fit polylines, meshes, or objects not drawn flat in the XY plane.");
            if (counts.Checked == 0)
                sb.Append("\n  No lines, arcs, circles or polylines to check.");

            List<GapFinding> findings = new List<GapFinding>(r.Findings);
            findings.Sort(CompareFindings);
            if (findings.Count > 0)
            {
                int shown = Math.Min(MaxListed, findings.Count);
                sb.Append("\n  Findings");
                if (shown < findings.Count)
                    sb.Append(" (first ").Append(shown).Append(" of ").Append(findings.Count).Append(')');
                sb.Append(':');
                for (int k = 0; k < shown; k++)
                {
                    GapFinding f = findings[k];
                    sb.Append("\n    ").Append(GapFinding.Describe(f.Kind).PadRight(17))
                      .Append((f.Size / upm).ToString("0.0000", Inv).PadLeft(9)).Append(" m")
                      .Append("  at ").Append(Num(f.X)).Append(',').Append(Num(f.Y));
                    if (r.NeedsTrim[f.Line] && r.NeedsExtend[f.Line])
                        sb.Append("  [object needs trim and extend]");
                }
            }
            sb.Append('\n');
            Print(ed, sb.ToString());
        }

        private static int CompareFindings(GapFinding a, GapFinding b)
        {
            // Trims first, then by kind, then largest problem first.
            if (a.IsTrim != b.IsTrim) return a.IsTrim ? -1 : 1;
            int c = ((int)a.Kind).CompareTo((int)b.Kind);
            if (c != 0) return c;
            return b.Size.CompareTo(a.Size);
        }

        // ------------------------------------------------------------------ GAPSET

        [CommandMethod("GAPSET", CommandFlags.Modal)]
        public void GapSet()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            while (true)
            {
                Print(ed, "\nCurrent settings:" +
                    "\n  Short     short line / overshoot length = " + Num(GapSettings.ShortLengthM) + " m" +
                    "\n  Gap       max gap to report             = " + Num(GapSettings.MaxGapM) + " m" +
                    "\n  Tolerance touch tolerance               = " + Num(GapSettings.TouchTolM) + " m" +
                    "\n  Units     drawing units per metre       = " +
                    (GapSettings.UnitsPerMetre > 0 ? Num(GapSettings.UnitsPerMetre) : "0 (automatic from INSUNITS)"));

                PromptKeywordOptions pko = new PromptKeywordOptions("\nChange setting or press Enter to finish");
                pko.Keywords.Add("Short");
                pko.Keywords.Add("Gap");
                pko.Keywords.Add("Tolerance");
                pko.Keywords.Add("Units");
                pko.Keywords.Add("Defaults");
                pko.AllowNone = true;
                PromptResult pr = ed.GetKeywords(pko);
                if (pr.Status != PromptStatus.OK) break;

                double v;
                switch (pr.StringResult)
                {
                    case "Short":
                        if (AskDouble(ed, "Short line / overshoot length in metres", GapSettings.ShortLengthM, false, out v))
                            GapSettings.ShortLengthM = v;
                        break;
                    case "Gap":
                        if (AskDouble(ed, "Max gap to report in metres", GapSettings.MaxGapM, false, out v))
                            GapSettings.MaxGapM = v;
                        break;
                    case "Tolerance":
                        if (AskDouble(ed, "Touch tolerance in metres", GapSettings.TouchTolM, true, out v))
                            GapSettings.TouchTolM = v;
                        break;
                    case "Units":
                        if (AskDouble(ed, "Drawing units per metre (0 = automatic from INSUNITS)", GapSettings.UnitsPerMetre, true, out v))
                            GapSettings.UnitsPerMetre = v;
                        break;
                    case "Defaults":
                        GapSettings.Reset();
                        break;
                }

                if (GapSettings.TouchTolM >= GapSettings.ShortLengthM)
                    Print(ed, "\nWarning: the touch tolerance is not smaller than the short length, so overshoots cannot be found.");
                if (GapSettings.TouchTolM >= GapSettings.MaxGapM)
                    Print(ed, "\nWarning: the touch tolerance is not smaller than the max gap, so gaps cannot be found.");
            }
        }

        private static bool AskDouble(Editor ed, string message, double current, bool allowZero, out double value)
        {
            value = current;
            PromptDoubleOptions pdo = new PromptDoubleOptions("\n" + message);
            pdo.AllowNegative = false;
            pdo.AllowZero = allowZero;
            pdo.DefaultValue = current;
            pdo.UseDefaultValue = true;
            PromptDoubleResult res = ed.GetDouble(pdo);
            if (res.Status != PromptStatus.OK) return false;
            value = res.Value;
            return true;
        }

        // ------------------------------------------------------------------ GAPRESET

        [CommandMethod("GAPRESET", CommandFlags.Modal)]
        public void GapReset()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;
            try
            {
                int restored, skipped;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    RestoreMarked(tr, db, new LayerLockCache(tr), out restored, out skipped);
                    tr.Commit();
                }
                string msg = "\nGAPRESET: " + restored + " object(s) restored to their original colour.";
                if (skipped > 0)
                    msg += "\n  " + skipped + " marked object(s) are on locked layers and were left as they are. Unlock and run GAPRESET again.";
                Print(ed, msg + "\n");
            }
            catch (System.Exception ex)
            {
                Print(ed, "\nGAPRESET failed: " + ex.Message + "\n");
            }
        }

        // ------------------------------------------------------------------ colour / XData

        /// <summary>
        /// Restores every line, arc, circle and polyline in model space and all layouts that
        /// carries our XData. Objects on locked layers are left unchanged (and keep their XData).
        /// </summary>
        private static void RestoreMarked(Transaction tr, Database db, LayerLockCache locks,
                                          out int restored, out int skipped)
        {
            restored = 0;
            skipped = 0;

            RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (!rat.Has(AppName)) return;   // nothing was ever marked in this drawing

            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId btrId in bt)
            {
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                if (!btr.IsLayout) continue;
                foreach (ObjectId id in btr)
                {
                    if (!IsCandidate(id.ObjectClass)) continue;
                    Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;
                    using (ResultBuffer rb = ent.GetXDataForApplication(AppName))
                    {
                        if (rb == null) continue;
                        if (locks.IsLocked(ent.LayerId) || !TryUpgradeOpen(ent))
                        {
                            skipped++;
                            continue;
                        }
                        ent.Color = ColorFromXData(rb.AsArray());
                        // Setting XData to just the app name removes this app's XData.
                        using (ResultBuffer clear = new ResultBuffer(
                            new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppName)))
                        {
                            ent.XData = clear;
                        }
                        restored++;
                    }
                }
            }
        }

        private static bool HasMarkXData(Entity ent)
        {
            using (ResultBuffer rb = ent.GetXDataForApplication(AppName))
            {
                return rb != null;
            }
        }

        /// <summary>
        /// XData layout: 1001 app, 1070 version, 1071 colour method, 1070 ACI index,
        /// 1071 RGB (0xRRGGBB), 1000 "book|name" for colour-book colours or "-".
        /// </summary>
        private static void SaveOriginalColor(Entity ent)
        {
            Color c = ent.Color;
            int rgb = (c.Red << 16) | (c.Green << 8) | c.Blue;
            string names = "-";
            if (c.ColorMethod == ColorMethod.ByColor && !string.IsNullOrEmpty(c.BookName))
                names = c.BookName + "|" + c.ColorName;

            using (ResultBuffer rb = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppName),
                new TypedValue((int)DxfCode.ExtendedDataInteger16, XDataVersion),
                new TypedValue((int)DxfCode.ExtendedDataInteger32, (int)c.ColorMethod),
                new TypedValue((int)DxfCode.ExtendedDataInteger16, c.ColorIndex),
                new TypedValue((int)DxfCode.ExtendedDataInteger32, rgb),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, names)))
            {
                ent.XData = rb;
            }
        }

        private static Color ColorFromXData(TypedValue[] v)
        {
            try
            {
                if (v == null || v.Length < 6) return ByLayer();
                ColorMethod method = (ColorMethod)Convert.ToInt32(v[2].Value, Inv);
                short index = Convert.ToInt16(v[3].Value, Inv);
                int rgb = Convert.ToInt32(v[4].Value, Inv);
                string names = Convert.ToString(v[5].Value, Inv);

                switch (method)
                {
                    case ColorMethod.ByLayer:
                        return ByLayer();
                    case ColorMethod.ByBlock:
                        return Color.FromColorIndex(ColorMethod.ByBlock, 0);
                    case ColorMethod.ByAci:
                        return Color.FromColorIndex(ColorMethod.ByAci, index);
                    case ColorMethod.ByColor:
                        int bar = names == null ? -1 : names.IndexOf('|');
                        if (bar > 0)
                        {
                            try
                            {
                                return Color.FromNames(names.Substring(bar + 1), names.Substring(0, bar));
                            }
                            catch (System.Exception)
                            {
                                // Colour book not available: fall back to the stored RGB value.
                            }
                        }
                        return Color.FromRgb((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
                    default:
                        return Color.FromColorIndex(method, index);
                }
            }
            catch (System.Exception)
            {
                return ByLayer();
            }
        }

        private static Color ByLayer()
        {
            return Color.FromColorIndex(ColorMethod.ByLayer, 256);
        }

        private static void EnsureRegApp(Transaction tr, Database db)
        {
            RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (rat.Has(AppName)) return;
            rat.UpgradeOpen();
            RegAppTableRecord rec = new RegAppTableRecord();
            rec.Name = AppName;
            rat.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }

        /// <summary>Upgrades a read-open object to write. Returns false if its layer is locked.</summary>
        private static bool TryUpgradeOpen(DBObject obj)
        {
            if (obj.IsWriteEnabled) return true;
            try
            {
                obj.UpgradeOpen();
                return true;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                if (ex.ErrorStatus == ErrorStatus.OnLockedLayer) return false;
                throw;
            }
        }

        /// <summary>Caches LayerTableRecord.IsLocked per layer for the life of one transaction.</summary>
        private sealed class LayerLockCache
        {
            private readonly Transaction tr;
            private readonly Dictionary<ObjectId, bool> cache = new Dictionary<ObjectId, bool>();

            public LayerLockCache(Transaction transaction)
            {
                tr = transaction;
            }

            public bool IsLocked(ObjectId layerId)
            {
                bool locked;
                if (cache.TryGetValue(layerId, out locked)) return locked;
                LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(layerId, OpenMode.ForRead);
                locked = ltr.IsLocked;
                cache.Add(layerId, locked);
                return locked;
            }
        }

        // ------------------------------------------------------------------ output helpers

        internal static void Print(Editor ed, string text)
        {
            // Pass the text as an argument so braces in it are never read as format items.
            ed.WriteMessage("{0}", text);
        }

        private static string Num(double v)
        {
            return v.ToString("0.######", Inv);
        }
    }
}
