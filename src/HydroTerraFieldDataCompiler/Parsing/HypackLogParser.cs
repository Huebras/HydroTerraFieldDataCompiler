using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using HydroTerraFieldDataCompiler.Models;

namespace HydroTerraFieldDataCompiler.Parsing;

public static class HypackLogParser
{
    private static readonly Regex RawReferenceRegex = new(
        "(?i)(?:\"(?<quoted>[^\"]+\\.raw)\"|(?<plain>[^\\s,;|]+\\.raw))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ExplicitLineRegex = new(
        "(?i)(?:LNN|LINE(?:NAME)?|SURVEYLINE)\\s*[:=]?\\s*\"?(?<line>[^\",;|\\r\\n]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static List<string> DiscoverReferencedRawFiles(string logPath)
    {
        var discovered = new List<string>();
        if (string.IsNullOrWhiteSpace(logPath) || !File.Exists(logPath)) return discovered;

        string logDirectory = Path.GetDirectoryName(Path.GetFullPath(logPath)) ?? string.Empty;
        string[] lines;
        try
        {
            lines = File.ReadAllLines(logPath);
        }
        catch
        {
            return discovered;
        }

        var references = new List<string>();
        foreach (string line in lines)
        {
            foreach (Match match in RawReferenceRegex.Matches(line))
            {
                string token = match.Groups["quoted"].Success
                    ? match.Groups["quoted"].Value
                    : match.Groups["plain"].Value;

                token = token.Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(token)) references.Add(token);
            }
        }

        if (references.Count == 0) return discovered;

        // Build a filename index once. HYPACK LOG files often contain only a RAW
        // filename or an old relative path, while the actual data is in a nearby
        // survey-day subfolder.
        Dictionary<string, List<string>>? filesByName = null;
        foreach (string reference in references.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string normalized = reference.Replace('/', Path.DirectorySeparatorChar)
                                         .Replace('\\', Path.DirectorySeparatorChar);
            string? resolved = null;

            try
            {
                if (Path.IsPathRooted(normalized) && File.Exists(normalized))
                {
                    resolved = Path.GetFullPath(normalized);
                }
                else
                {
                    string relativeCandidate = Path.GetFullPath(Path.Combine(logDirectory, normalized));
                    if (File.Exists(relativeCandidate)) resolved = relativeCandidate;
                }
            }
            catch
            {
                // Invalid or legacy path text; use the filename fallback below.
            }

            if (resolved == null)
            {
                filesByName ??= BuildRawFileIndex(logDirectory);
                string fileName = Path.GetFileName(normalized);
                if (!string.IsNullOrWhiteSpace(fileName) &&
                    filesByName.TryGetValue(fileName, out List<string>? matches) &&
                    matches.Count > 0)
                {
                    // Prefer the nearest path to the LOG file, then alphabetically
                    // for deterministic behavior when duplicate names exist.
                    resolved = matches
                        .OrderBy(path => RelativeDepth(logDirectory, path))
                        .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                        .First();
                }
            }

            if (resolved != null &&
                !discovered.Contains(resolved, StringComparer.OrdinalIgnoreCase))
            {
                discovered.Add(resolved);
            }
        }

        return discovered;
    }

    private static Dictionary<string, List<string>> BuildRawFileIndex(string rootDirectory)
    {
        var index = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory)) return index;

        try
        {
            foreach (string path in Directory.EnumerateFiles(rootDirectory, "*.raw", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(path);
                if (!index.TryGetValue(name, out List<string>? paths))
                {
                    paths = new List<string>();
                    index[name] = paths;
                }
                paths.Add(Path.GetFullPath(path));
            }
        }
        catch
        {
            // Access to one or more folders may be restricted. Directly resolved
            // references still work even if the fallback index cannot be completed.
        }

        return index;
    }

    private static int RelativeDepth(string rootDirectory, string path)
    {
        try
        {
            string relative = Path.GetRelativePath(rootDirectory, path);
            return relative.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return int.MaxValue;
        }
    }

    public static List<HypackLogSummary> Parse(
        IEnumerable<string> directLogPaths,
        IEnumerable<string> importedSurveyPaths,
        IReadOnlyCollection<RawFileSummary> loadedRawFiles,
        List<QaFinding> findings)
    {
        var summaries = new List<HypackLogSummary>();
        var loadedByName = loadedRawFiles
            .Where(f => !string.IsNullOrWhiteSpace(f.DisplayName))
            .GroupBy(f => Path.GetFileName(f.DisplayName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (string path in directLogPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path))
            {
                var missing = new HypackLogSummary { SourcePath = path, DisplayName = Path.GetFileName(path), Status = "Missing" };
                summaries.Add(missing);
                findings.Add(new QaFinding
                {
                    RuleId = "LOG-MISSING",
                    Severity = "Warning",
                    Category = "HYPACK LOG",
                    FileName = missing.DisplayName,
                    Description = "The HYPACK LOG file cannot be found at the stored path.",
                    Evidence = path
                });
                continue;
            }

            try
            {
                using Stream stream = File.OpenRead(path);
                summaries.Add(ParseStream(path, Path.GetFileName(path), string.Empty, stream, loadedByName, findings));
            }
            catch (Exception ex)
            {
                summaries.Add(ParseFailure(path, Path.GetFileName(path), string.Empty, ex, findings));
            }
        }

        foreach (string zipPath in importedSurveyPaths
            .Where(p => p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(zipPath)) continue;
            try
            {
                using ZipArchive archive = ZipFile.OpenRead(zipPath);
                foreach (ZipArchiveEntry entry in archive.Entries.Where(e => e.FullName.EndsWith(".log", StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        using Stream stream = entry.Open();
                        summaries.Add(ParseStream(zipPath, $"{Path.GetFileName(zipPath)} :: {entry.FullName}", entry.FullName, stream, loadedByName, findings));
                    }
                    catch (Exception ex)
                    {
                        summaries.Add(ParseFailure(zipPath, $"{Path.GetFileName(zipPath)} :: {entry.FullName}", entry.FullName, ex, findings));
                    }
                }
            }
            catch (Exception ex)
            {
                findings.Add(new QaFinding
                {
                    RuleId = "LOG-ZIP-READ",
                    Severity = "Warning",
                    Category = "HYPACK LOG",
                    FileName = Path.GetFileName(zipPath),
                    Description = "The ZIP archive could not be inspected for embedded HYPACK LOG files.",
                    Evidence = ex.Message
                });
            }
        }

        return summaries;
    }

    private static HypackLogSummary ParseStream(
        string sourcePath,
        string displayName,
        string archiveEntryName,
        Stream stream,
        IReadOnlyDictionary<string, RawFileSummary> loadedByName,
        List<QaFinding> findings)
    {
        var summary = new HypackLogSummary
        {
            SourcePath = sourcePath,
            DisplayName = displayName,
            ArchiveEntryName = archiveEntryName,
            IsEmbeddedInZip = !string.IsNullOrWhiteSpace(archiveEntryName),
            Status = "Not parsed"
        };

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        summary.SizeBytes = memory.Length;
        memory.Position = 0;
        summary.Sha256 = Convert.ToHexString(SHA256.HashData(memory)).ToLowerInvariant();
        memory.Position = 0;

        int order = 0;
        using var reader = new StreamReader(memory, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
        while (reader.ReadLine() is string rawLine)
        {
            foreach (Match match in RawReferenceRegex.Matches(rawLine))
            {
                string token = match.Groups["quoted"].Success ? match.Groups["quoted"].Value : match.Groups["plain"].Value;
                token = token.Trim().Trim('"').Replace('/', Path.DirectorySeparatorChar);
                string fileName = Path.GetFileName(token);
                if (string.IsNullOrWhiteSpace(fileName)) continue;

                string lineName = string.Empty;
                Match lineMatch = ExplicitLineRegex.Match(rawLine);
                if (lineMatch.Success) lineName = lineMatch.Groups["line"].Value.Trim().Trim('"');

                if (summary.References.Any(r => r.RawFileName.Equals(fileName, StringComparison.OrdinalIgnoreCase))) continue;

                summary.References.Add(new HypackLogReference
                {
                    Order = ++order,
                    RawFileName = fileName,
                    ReferencedPath = token,
                    LineName = lineName,
                    Found = loadedByName.ContainsKey(fileName),
                    SourceText = rawLine.Trim()
                });
            }
        }

        summary.ReferencedRawCount = summary.References.Count;
        summary.FoundRawCount = summary.References.Count(r => r.Found);
        summary.MissingRawCount = summary.References.Count(r => !r.Found);
        var referenceNames = summary.References.Select(r => r.RawFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        summary.UnlistedLoadedRawCount = loadedByName.Keys.Count(name => !referenceNames.Contains(name));
        summary.Status = summary.ReferencedRawCount == 0 ? "No RAW references found" : summary.MissingRawCount > 0 ? $"{summary.MissingRawCount} missing" : "Matched";

        foreach (HypackLogReference missing in summary.References.Where(r => !r.Found))
        {
            findings.Add(new QaFinding
            {
                RuleId = "LOG-RAW-MISSING",
                Severity = "Warning",
                Category = "HYPACK LOG",
                FileName = summary.DisplayName,
                SurveyLine = missing.LineName,
                Description = $"The LOG references RAW file '{missing.RawFileName}', but that file was not loaded.",
                Evidence = missing.SourceText
            });
        }

        if (summary.UnlistedLoadedRawCount > 0 && summary.ReferencedRawCount > 0)
        {
            string unlisted = string.Join(", ", loadedByName.Keys.Where(name => !referenceNames.Contains(name)).OrderBy(name => name).Take(20));
            findings.Add(new QaFinding
            {
                RuleId = "LOG-RAW-UNLISTED",
                Severity = "Info",
                Category = "HYPACK LOG",
                FileName = summary.DisplayName,
                Description = $"{summary.UnlistedLoadedRawCount} loaded RAW file(s) are not listed in this LOG.",
                Evidence = unlisted
            });
        }

        return summary;
    }

    private static HypackLogSummary ParseFailure(string sourcePath, string displayName, string archiveEntryName, Exception ex, List<QaFinding> findings)
    {
        findings.Add(new QaFinding
        {
            RuleId = "LOG-PARSE",
            Severity = "Warning",
            Category = "HYPACK LOG",
            FileName = displayName,
            Description = "The HYPACK LOG file could not be parsed.",
            Evidence = ex.Message
        });
        return new HypackLogSummary
        {
            SourcePath = sourcePath,
            DisplayName = displayName,
            ArchiveEntryName = archiveEntryName,
            IsEmbeddedInZip = !string.IsNullOrWhiteSpace(archiveEntryName),
            Status = "Parse error"
        };
    }

    public static void ApplyLogOrder(List<RawFileSummary> files, IEnumerable<HypackLogSummary> logs)
    {
        var order = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int next = 0;
        foreach (HypackLogReference reference in logs.SelectMany(l => l.References).OrderBy(r => r.Order))
            if (!order.ContainsKey(reference.RawFileName)) order[reference.RawFileName] = next++;

        if (order.Count == 0) return;
        files.Sort((a, b) =>
        {
            bool aKnown = order.TryGetValue(Path.GetFileName(a.DisplayName), out int ai);
            bool bKnown = order.TryGetValue(Path.GetFileName(b.DisplayName), out int bi);
            if (aKnown && bKnown) return ai.CompareTo(bi);
            if (aKnown) return -1;
            if (bKnown) return 1;
            return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
        });
    }
}
