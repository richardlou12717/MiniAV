using System.Text;

namespace MiniAV.Engines.ClamAV;

/// <summary>
/// Windows CreateProcessW command-line encoding for a fixed executable and
/// individual argv elements. Never invoke a shell with the resulting string.
/// </summary>
internal static class WindowsArgumentEncoding
{
    internal static string Build(string executable, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        var result = new StringBuilder(Quote(executable));
        foreach (var arg in arguments)
        {
            result.Append(' ').Append(Quote(arg));
        }
        return result.ToString();
    }

    internal static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Contains('\0')) throw new ArgumentException("NUL is not a valid Windows argument.", nameof(value));
        var result = new StringBuilder("\"");
        int slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                slashes++;
                continue;
            }
            if (character == '"')
            {
                result.Append('\\', slashes * 2 + 1);
                result.Append('"');
                slashes = 0;
                continue;
            }
            result.Append('\\', slashes);
            slashes = 0;
            result.Append(character);
        }
        result.Append('\\', slashes * 2);
        result.Append('"');
        return result.ToString();
    }
}
