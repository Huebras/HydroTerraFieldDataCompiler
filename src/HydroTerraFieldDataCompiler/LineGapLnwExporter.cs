using System.Globalization;
using System.Text;
using HydroTerraFieldDataCompiler.Models;

namespace HydroTerraFieldDataCompiler;

public static class LineGapLnwExporter
{
    public static void Export(IEnumerable<LineCoverageResult> results, string lnwPath, double overlapFeet, double unitFactorMeters)
    {
        if (unitFactorMeters <= 0) unitFactorMeters = 0.3048006096012192;
        double overlap = overlapFeet * 0.3048 / unitFactorMeters;
        var lnw = new StringBuilder();
        var csv = new StringBuilder("Line,Gap,MissingStart,MissingEnd,MissingLength,Overlap,ExportStart,ExportEnd,ExportLineName,SourceFile\r\n");

        foreach (var r in results)
        {
            double dx = r.EndX - r.StartX, dy = r.EndY - r.StartY;
            double length = r.PlannedLength;
            if (length <= 0) continue;
            foreach (var gap in r.Gaps)
            {
                double exportStart = Math.Max(0, gap.StartChainage - overlap);
                double exportEnd = Math.Min(length, gap.EndChainage + overlap);
                double sx = r.StartX + dx * exportStart / length;
                double sy = r.StartY + dy * exportStart / length;
                double ex = r.StartX + dx * exportEnd / length;
                double ey = r.StartY + dy * exportEnd / length;
                string exportName = SanitizeLineName($"{r.LineName}_GAP{gap.GapNumber:00}");

                lnw.AppendLine("LIN 2");
                lnw.AppendLine($"PTS {F(sx)} {F(sy)}");
                lnw.AppendLine($"PTS {F(ex)} {F(ey)}");
                lnw.AppendLine($"LBP {F(sx)} {F(sy)}");
                lnw.AppendLine($"LNN {exportName}");
                lnw.AppendLine("EOL");

                csv.AppendLine(string.Join(",", Csv(r.LineName), gap.GapNumber, F(gap.StartChainage), F(gap.EndChainage), F(gap.MissingLength), F(overlap), F(exportStart), F(exportEnd), Csv(exportName), Csv(r.SourceFile)));
            }
        }

        File.WriteAllText(lnwPath, lnw.ToString(), Encoding.ASCII);
        File.WriteAllText(Path.ChangeExtension(lnwPath, ".csv"), csv.ToString(), Encoding.UTF8);
    }

    private static string SanitizeLineName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(c => invalid.Contains(c) || char.IsWhiteSpace(c) ? '_' : c).ToArray());
    }

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}
