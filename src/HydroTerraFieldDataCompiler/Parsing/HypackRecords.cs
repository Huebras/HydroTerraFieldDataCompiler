using System.Globalization;
using HydroTerraFieldDataCompiler.Models;

namespace HydroTerraFieldDataCompiler.Parsing;

public abstract class HypackRecord
{
    public string RecordType { get; init; } = string.Empty;
    public string RawText { get; init; } = string.Empty;
    public int SourceLineNumber { get; init; }
    public int? DeviceId { get; init; }
    public double? SecondsOfDay { get; init; }
}

public sealed class UnknownHypackRecord : HypackRecord { }

public sealed class DeviceHypackRecord : HypackRecord
{
    public int? InterfaceType { get; init; }
    public string Description { get; init; } = string.Empty;
    public string DriverPath { get; init; } = string.Empty;
    public string DriverVersion { get; init; } = string.Empty;
}

public sealed class OffsetHypackRecord : HypackRecord
{
    public double? Starboard { get; init; }
    public double? Forward { get; init; }
    public double? Vertical { get; init; }
    public double? Yaw { get; init; }
    public double? Roll { get; init; }
    public double? Pitch { get; init; }
    public double? Latency { get; init; }
}

public sealed class QualityHypackRecord : HypackRecord
{
    public double? Hdop { get; init; }
    public int? SatelliteCount { get; init; }
    public int? ModeCode { get; init; }
    public int? DeclaredValueCount { get; init; }
    public double? CorrectionAgeSeconds { get; init; }
    public List<string> RawQualityFields { get; init; } = new();
    public GnssSolutionType SolutionType { get; init; }
}

public sealed class PositionHypackRecord : HypackRecord
{
    public double? X { get; init; }
    public double? Y { get; init; }
}

public sealed class SurveyLineHypackRecord : HypackRecord
{
    public string LineName { get; init; } = string.Empty;
}

public sealed class EchosounderHypackRecord : HypackRecord
{
    public double? Depth1 { get; init; }
    public double? Depth2 { get; init; }
}

public sealed class TideHypackRecord : HypackRecord
{
    public double? TideValue { get; init; }
}

public sealed class FixHypackRecord : HypackRecord
{
    public int? EventNumber { get; init; }
    public double? X { get; init; }
    public double? Y { get; init; }
}

public sealed class PrdHypackRecord : HypackRecord
{
    public List<string> Parameters { get; init; } = new();
}

public sealed class HypackRawReader
{
    private static readonly HashSet<string> LineTypes = new(StringComparer.OrdinalIgnoreCase) { "LIN", "LNW", "LNN" };

    public HypackRecord Parse(string text, int sourceLineNumber)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0) return new UnknownHypackRecord { RawText = text, SourceLineNumber = sourceLineNumber };
        string[] fields = trimmed.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length == 0) return new UnknownHypackRecord { RawText = text, SourceLineNumber = sourceLineNumber };
        string type = fields[0].Trim().ToUpperInvariant();

        if (type == "QUA") return ParseQua(fields, text, sourceLineNumber);
        if (type == "POS") return ParsePos(fields, text, sourceLineNumber);
        if (type == "DEV") return ParseDev(fields, text, sourceLineNumber);
        if (type == "OFF") return ParseOff(fields, text, sourceLineNumber);
        if (type == "EC1" || type == "EC2") return ParseEc(fields, text, sourceLineNumber, type);
        if (type == "TID") return ParseTid(fields, text, sourceLineNumber);
        if (type == "FIX") return ParseFix(fields, text, sourceLineNumber);
        if (type == "PRD") return ParsePrd(fields, text, sourceLineNumber);
        if (LineTypes.Contains(type)) return ParseLine(fields, text, sourceLineNumber);
        return new UnknownHypackRecord
        {
            RecordType = type,
            RawText = text,
            SourceLineNumber = sourceLineNumber,
            DeviceId = TryInt(fields, 1),
            SecondsOfDay = TryDouble(fields, 2)
        };
    }

    private static DeviceHypackRecord ParseDev(string[] fields, string text, int lineNumber)
    {
        // DEV device interfaceType "description" flags driverPath version
        // Parse the quoted description directly because driver paths can contain spaces.
        string description = string.Empty;
        int firstQuote = text.IndexOf('"');
        int secondQuote = firstQuote >= 0 ? text.IndexOf('"', firstQuote + 1) : -1;
        if (firstQuote >= 0 && secondQuote > firstQuote)
            description = text.Substring(firstQuote + 1, secondQuote - firstQuote - 1).Trim();
        else if (fields.Length > 3)
            description = fields[3].Trim('"');

        string driverPath = string.Empty;
        string driverVersion = string.Empty;
        if (secondQuote >= 0 && secondQuote + 1 < text.Length)
        {
            string remainder = text[(secondQuote + 1)..].Trim();
            // Remove the numeric flags value following the description.
            int firstSpace = remainder.IndexOfAny(new[] { ' ', '	' });
            if (firstSpace >= 0)
            {
                remainder = remainder[(firstSpace + 1)..].Trim();
                int lastSpace = remainder.LastIndexOfAny(new[] { ' ', '	' });
                if (lastSpace >= 0)
                {
                    driverPath = remainder[..lastSpace].Trim();
                    driverVersion = remainder[(lastSpace + 1)..].Trim();
                }
                else driverPath = remainder;
            }
        }

        return new DeviceHypackRecord
        {
            RecordType = "DEV",
            RawText = text,
            SourceLineNumber = lineNumber,
            DeviceId = TryInt(fields, 1),
            InterfaceType = TryInt(fields, 2),
            Description = description,
            DriverPath = driverPath,
            DriverVersion = driverVersion
        };
    }

    private static OffsetHypackRecord ParseOff(string[] fields, string text, int lineNumber)
    {
        // HYPACK header layout: OFF device starboard forward vertical yaw roll pitch latency
        return new OffsetHypackRecord
        {
            RecordType = "OFF",
            RawText = text,
            SourceLineNumber = lineNumber,
            DeviceId = TryInt(fields, 1),
            Starboard = TryDouble(fields, 2),
            Forward = TryDouble(fields, 3),
            Vertical = TryDouble(fields, 4),
            Yaw = TryDouble(fields, 5),
            Roll = TryDouble(fields, 6),
            Pitch = TryDouble(fields, 7),
            Latency = TryDouble(fields, 8)
        };
    }

    private static QualityHypackRecord ParseQua(string[] fields, string text, int lineNumber)
    {
        // HYPACK layout observed in current survey files:
        // QUA device time solutionCode valueCount hdop satellites correctionAge [additional values]
        // Example: QUA 0 42676.700 7 6.000 0.500 29.000 4.000 0.000 0.000 0.000
        int? device = TryInt(fields, 1);
        double? time = TryDouble(fields, 2);
        int? mode = TryIntFlexible(fields, 3);
        int? declaredCount = TryIntFlexible(fields, 4);
        double? hdop = TryDouble(fields, 5);
        int? satellites = TryIntFlexible(fields, 6);
        double? correctionAge = TryDouble(fields, 7);

        return new QualityHypackRecord
        {
            RecordType = "QUA",
            RawText = text,
            SourceLineNumber = lineNumber,
            DeviceId = device,
            SecondsOfDay = time,
            DeclaredValueCount = declaredCount,
            Hdop = hdop,
            SatelliteCount = satellites,
            CorrectionAgeSeconds = correctionAge,
            ModeCode = mode,
            RawQualityFields = fields.Skip(3).ToList(),
            SolutionType = GnssSolutionType.Unknown
        };
    }

    private static EchosounderHypackRecord ParseEc(string[] fields, string text, int lineNumber, string recordType) => new()
    {
        RecordType = recordType, RawText = text, SourceLineNumber = lineNumber,
        DeviceId = TryInt(fields, 1), SecondsOfDay = TryDouble(fields, 2),
        Depth1 = TryDouble(fields, 3), Depth2 = TryDouble(fields, 4)
    };

    private static TideHypackRecord ParseTid(string[] fields, string text, int lineNumber) => new()
    {
        RecordType = "TID", RawText = text, SourceLineNumber = lineNumber,
        DeviceId = TryInt(fields, 1), SecondsOfDay = TryDouble(fields, 2),
        TideValue = TryDouble(fields, 3)
    };

    private static FixHypackRecord ParseFix(string[] fields, string text, int lineNumber) => new()
    {
        RecordType = "FIX", RawText = text, SourceLineNumber = lineNumber,
        DeviceId = TryInt(fields, 1), SecondsOfDay = TryDouble(fields, 2),
        EventNumber = TryIntFlexible(fields, 3), X = TryDouble(fields, 4), Y = TryDouble(fields, 5)
    };

    private static PrdHypackRecord ParsePrd(string[] fields, string text, int lineNumber) => new()
    {
        RecordType = "PRD", RawText = text, SourceLineNumber = lineNumber,
        DeviceId = TryInt(fields, 1), Parameters = fields.Skip(2).ToList()
    };

    private static PositionHypackRecord ParsePos(string[] fields, string text, int lineNumber) => new()
    {
        RecordType = "POS", RawText = text, SourceLineNumber = lineNumber,
        DeviceId = TryInt(fields, 1), SecondsOfDay = TryDouble(fields, 2),
        X = TryDouble(fields, 3), Y = TryDouble(fields, 4)
    };

    private static SurveyLineHypackRecord ParseLine(string[] fields, string text, int lineNumber)
    {
        string name = string.Empty;
        for (int i = fields.Length - 1; i >= 1; i--)
        {
            if (!double.TryParse(fields[i], NumberStyles.Float, CultureInfo.InvariantCulture, out _)) { name = fields[i].Trim('"'); break; }
        }
        return new SurveyLineHypackRecord { RecordType = fields[0].ToUpperInvariant(), RawText = text, SourceLineNumber = lineNumber, DeviceId = TryInt(fields, 1), SecondsOfDay = TryDouble(fields, 2), LineName = name };
    }

    public static GnssSolutionType MapQuality(int? mode, GnssQualityProfile profile)
    {
        if (!mode.HasValue) return GnssSolutionType.Unknown;
        if (profile == GnssQualityProfile.ApplanixPosMv)
            return mode.Value switch { 0 => GnssSolutionType.Invalid, 1 => GnssSolutionType.Autonomous, 2 => GnssSolutionType.Differential, 3 => GnssSolutionType.Float, 4 => GnssSolutionType.Fixed, 5 => GnssSolutionType.Fixed, 7 => GnssSolutionType.Fixed, _ => GnssSolutionType.Unknown };
        if (profile == GnssQualityProfile.VrsNetwork)
            return mode.Value switch { 0 => GnssSolutionType.Invalid, 1 => GnssSolutionType.Autonomous, 2 => GnssSolutionType.Differential, 4 => GnssSolutionType.Fixed, 5 => GnssSolutionType.Float, 6 => GnssSolutionType.DeadReckoning, 7 => GnssSolutionType.Fixed, _ => GnssSolutionType.Unknown };
        return mode.Value switch { 0 => GnssSolutionType.Invalid, 1 => GnssSolutionType.Autonomous, 2 => GnssSolutionType.Differential, 3 => GnssSolutionType.Differential, 4 => GnssSolutionType.Fixed, 5 => GnssSolutionType.Float, 6 => GnssSolutionType.DeadReckoning, 7 => GnssSolutionType.Unknown, 8 => GnssSolutionType.Invalid, _ => GnssSolutionType.Unknown };
    }

    private static bool IsKnownMode(int? mode) => mode is >= 0 and <= 8;
    private static int? TryInt(string[] fields, int index) => index < fields.Length && int.TryParse(fields[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : null;
    private static int? TryIntFlexible(string[] fields, int index)
    {
        if (index >= fields.Length) return null;
        if (int.TryParse(fields[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer)) return integer;
        if (double.TryParse(fields[index], NumberStyles.Float, CultureInfo.InvariantCulture, out double numeric) && Math.Abs(numeric - Math.Round(numeric)) < 0.000001)
            return (int)Math.Round(numeric);
        return null;
    }
    private static double? TryDouble(string[] fields, int index) => index < fields.Length && double.TryParse(fields[index], NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : null;
}
