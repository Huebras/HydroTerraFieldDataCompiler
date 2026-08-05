using System.Globalization;
using System.Text;
using HydroTerraFieldDataCompiler.Models;

namespace HydroTerraFieldDataCompiler;

public static class LineGapDxfExporter
{
    public static void Export(IEnumerable<LineCoverageResult> results, string dxfPath, double overlapFeet, double unitFactorMeters)
    {
        if (unitFactorMeters <= 0) unitFactorMeters = 0.3048006096012192;
        double overlap = overlapFeet * 0.3048 / unitFactorMeters;
        var csvPath = Path.ChangeExtension(dxfPath, ".csv");
        var dxf = new StringBuilder();
        dxf.AppendLine("0\nSECTION\n2\nHEADER\n0\nENDSEC\n0\nSECTION\n2\nENTITIES");
        var csv = new StringBuilder("Line,Gap,MissingStart,MissingEnd,MissingLength,Overlap,ExportStart,ExportEnd,SourceFile\r\n");
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
                dxf.AppendLine("0\nLINE\n8\nUNSURVEYED_LINES");
                dxf.AppendLine("10\n" + F(sx) + "\n20\n" + F(sy) + "\n30\n0");
                dxf.AppendLine("11\n" + F(ex) + "\n21\n" + F(ey) + "\n31\n0");
                csv.AppendLine(string.Join(",", Csv(r.LineName), gap.GapNumber, F(gap.StartChainage), F(gap.EndChainage), F(gap.MissingLength), F(overlap), F(exportStart), F(exportEnd), Csv(r.SourceFile)));
            }
        }
        dxf.AppendLine("0\nENDSEC\n0\nEOF");
        File.WriteAllText(dxfPath, dxf.ToString(), Encoding.ASCII);
        File.WriteAllText(csvPath, csv.ToString(), Encoding.UTF8);
    }
    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}
