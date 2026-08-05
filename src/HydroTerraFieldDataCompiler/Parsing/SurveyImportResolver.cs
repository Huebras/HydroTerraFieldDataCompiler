using System.Text.RegularExpressions;

namespace HydroTerraFieldDataCompiler.Parsing;

public sealed class SurveyImportResolution
{
    public List<string> RawOrZipPaths { get; } = new();
    public List<string> LogPaths { get; } = new();
    public List<string> RawFilesDiscoveredFromLogs { get; } = new();
    public List<string> UnresolvedLogReferences { get; } = new();
}

/// <summary>
/// Expands a mixed RAW / LOG / ZIP selection into the common survey-import lists.
/// A direct HYPACK LOG file is treated as an entry point: RAW files referenced by
/// the LOG are resolved relative to the LOG location and added to the normal RAW
/// import pipeline.
/// </summary>
public static class SurveyImportResolver
{
    private static readonly Regex RawReferenceRegex = new(
        "(?i)(?:\"(?<quoted>[^\"]+\\.raw)\"|(?<plain>[^\\s,;|]+\\.raw))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static SurveyImportResolution Resolve(IEnumerable<string> selectedPaths)
    {
        var result = new SurveyImportResolution();
        var rawOrZip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var logs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unresolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string originalPath in selectedPaths.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            string path = Path.GetFullPath(originalPath.Trim());
            string extension = Path.GetExtension(path);

            if (extension.Equals(".raw", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(path)) rawOrZip.Add(path);
                continue;
            }

            if (!extension.Equals(".log", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                continue;

            logs.Add(path);
            foreach (LogRawReference reference in ReadRawReferences(path))
            {
                string? resolved = ResolveReference(path, reference.ReferenceText);
                if (resolved == null)
                {
                    unresolved.Add($"{Path.GetFileName(path)} :: {reference.ReferenceText}");
                    continue;
                }

                rawOrZip.Add(resolved);
                discovered.Add(resolved);
            }
        }

        result.RawOrZipPaths.AddRange(rawOrZip.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
        result.LogPaths.AddRange(logs.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
        result.RawFilesDiscoveredFromLogs.AddRange(discovered.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
        result.UnresolvedLogReferences.AddRange(unresolved.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
        return result;
    }

    private static IEnumerable<LogRawReference> ReadRawReferences(string logPath)
    {
        int order = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string sourceLine in File.ReadLines(logPath))
        {
            string line = sourceLine.Trim();
            if (line.Length == 0) continue;

            bool foundOnLine = false;
            foreach (Match match in RawReferenceRegex.Matches(line))
            {
                string token = match.Groups["quoted"].Success
                    ? match.Groups["quoted"].Value
                    : match.Groups["plain"].Value;

                token = CleanReference(token);
                if (token.Length == 0 || !seen.Add(token)) continue;
                foundOnLine = true;
                yield return new LogRawReference(++order, token);
            }

            // Some HYPACK LOG files contain only a bare file stem per line.
            // Accept those as RAW references when the line otherwise looks like a filename.
            if (!foundOnLine && LooksLikeBareFileReference(line))
            {
                string token = CleanReference(line);
                if (Path.GetExtension(token).Length == 0) token += ".RAW";
                if (seen.Add(token)) yield return new LogRawReference(++order, token);
            }
        }
    }

    private static string CleanReference(string value)
    {
        string token = value.Trim().Trim('"', '\'', ',', ';', '|');
        token = token.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return token;
    }

    private static bool LooksLikeBareFileReference(string line)
    {
        string token = line.Trim().Trim('"', '\'', ',', ';', '|');
        if (token.Length == 0 || token.Any(char.IsWhiteSpace)) return false;

        string extension = Path.GetExtension(token);
        if (extension.Equals(".raw", StringComparison.OrdinalIgnoreCase)) return true;
        if (extension.Length != 0) return false;

        // Common HYPACK file stems include letters, digits, underscores, plus signs and dashes.
        return token.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '+' or '.');
    }

    private static string? ResolveReference(string logPath, string referenceText)
    {
        string logDirectory = Path.GetDirectoryName(logPath) ?? Environment.CurrentDirectory;
        string reference = Environment.ExpandEnvironmentVariables(referenceText.Trim());
        if (Path.GetExtension(reference).Length == 0) reference += ".RAW";

        // 1. A valid absolute path recorded by HYPACK.
        if (Path.IsPathRooted(reference))
        {
            try
            {
                string absolute = Path.GetFullPath(reference);
                if (File.Exists(absolute)) return absolute;
            }
            catch
            {
                // Continue with filename-based fallback for stale acquisition paths.
            }
        }

        // 2. A path relative to the LOG file.
        try
        {
            string relative = Path.GetFullPath(Path.Combine(logDirectory, reference));
            if (File.Exists(relative)) return relative;
        }
        catch
        {
            // Continue with filename search.
        }

        // 3. A filename-only or stale-path fallback within the survey tree.
        string fileName = Path.GetFileName(reference);
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        try
        {
            return Directory.EnumerateFiles(logDirectory, "*", SearchOption.AllDirectories)
                .Where(p => Path.GetFileName(p).Equals(fileName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => RelativeDepth(logDirectory, p))
                .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static int RelativeDepth(string root, string path)
    {
        try
        {
            string relative = Path.GetRelativePath(root, path);
            return relative.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return int.MaxValue;
        }
    }

    private readonly record struct LogRawReference(int Order, string ReferenceText);
}
