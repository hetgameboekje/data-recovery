using System.Text;

namespace RecoveryDAL.Configuration;

/// <summary>
/// Minimal .env parser:  KEY=value,  KEY="quoted \n value",  KEY='literal',  # comments,  optional "export ",
/// and ${OTHER_KEY} expansion (from earlier keys, then from real environment variables).
/// </summary>
public static class EnvFile
{
    public static Dictionary<string, string> Parse(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lineNo = 0;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            lineNo++;
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            if (line.StartsWith("export ", StringComparison.Ordinal)) line = line[7..].TrimStart();
            var eq = line.IndexOf('=');
            if (eq <= 0) throw new FormatException($".env line {lineNo}: expected KEY=value");
            var key = line[..eq].Trim();
            if (!key.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
                throw new FormatException($".env line {lineNo}: invalid key '{key}'");
            values[key] = ParseValue(line[(eq + 1)..].Trim(), values, lineNo);
        }
        return values;
    }

    static string ParseValue(string v, Dictionary<string, string> known, int lineNo)
    {
        if (v.Length >= 2 && v[0] == '\'' )
        {
            var end = v.IndexOf('\'', 1);
            if (end < 0) throw new FormatException($".env line {lineNo}: missing closing '");
            return v[1..end];                                  // single quotes: literal, no expansion
        }
        if (v.Length >= 1 && v[0] == '"')
        {
            var sb = new StringBuilder();
            int i = 1;
            for (; i < v.Length && v[i] != '"'; i++)
            {
                // Only real escapes are interpreted; any other backslash is kept, so Windows paths such as
                // (localdb)\MSSQLLocalDB or C:\data work inside double quotes.
                if (v[i] == '\\' && i + 1 < v.Length && v[i + 1] is 'n' or 't' or 'r' or '"' or '\\' or '$')
                {
                    i++;
                    sb.Append(v[i] switch { 'n' => '\n', 't' => '\t', 'r' => '\r', _ => v[i] });
                }
                else sb.Append(v[i]);
            }
            if (i >= v.Length) throw new FormatException($".env line {lineNo}: missing closing \"");
            return Expand(sb.ToString(), known);
        }
        var hash = v.IndexOf(" #", StringComparison.Ordinal);   // unquoted: " #" starts a comment
        if (hash >= 0) v = v[..hash].TrimEnd();
        return Expand(v, known);
    }

    static string Expand(string v, Dictionary<string, string> known)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < v.Length; i++)
        {
            if (v[i] == '$' && i + 1 < v.Length && v[i + 1] == '{')
            {
                var end = v.IndexOf('}', i + 2);
                if (end > 0)
                {
                    var name = v[(i + 2)..end];
                    sb.Append(known.TryGetValue(name, out var val) ? val : Environment.GetEnvironmentVariable(name) ?? "");
                    i = end;
                    continue;
                }
            }
            sb.Append(v[i]);
        }
        return sb.ToString();
    }
}
