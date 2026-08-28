using System.Text;

namespace Skyrim.RecordChain;

// Decodes the scalar QSettings INI values used for MO2 paths.
internal static class QSettingsValue
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    internal static string? DecodeString(string? raw, string context)
    {
        if (raw is null)
        {
            return null;
        }

        var value = DecodeIniString(raw.Trim(), context);
        if (value.StartsWith("@@", StringComparison.Ordinal))
        {
            return value[1..];
        }

        if (value.Equals("@Invalid()", StringComparison.Ordinal))
        {
            return null;
        }

        const string byteArrayPrefix = "@ByteArray(";
        if (value.StartsWith(byteArrayPrefix, StringComparison.Ordinal))
        {
            if (!value.EndsWith(')'))
            {
                throw Malformed(context, raw, "unterminated @ByteArray value");
            }

            return DecodeByteArray(value[byteArrayPrefix.Length..^1], context, raw);
        }

        if (value.StartsWith('@'))
        {
            throw Malformed(context, raw, "unsupported QSettings value type");
        }

        return value;
    }

    private static string DecodeIniString(string encoded, string context)
    {
        encoded = encoded.Trim();
        var unescapedQuotes = FindUnescapedQuotes(encoded);
        if (unescapedQuotes.Count > 0)
        {
            if (unescapedQuotes.Count != 2 ||
                unescapedQuotes[0] != 0 ||
                unescapedQuotes[1] != encoded.Length - 1)
            {
                throw Malformed(context, encoded, "invalid quoted string");
            }

            encoded = encoded[1..^1];
        }

        var decoded = new StringBuilder(encoded.Length);
        for (var index = 0; index < encoded.Length; index++)
        {
            var character = encoded[index];
            if (character != '\\')
            {
                decoded.Append(character);
                continue;
            }

            if (++index >= encoded.Length)
            {
                break;
            }

            var escapedCharacter = encoded[index];
            var decodedCharacter = escapedCharacter switch
            {
                'a' => '\a',
                'b' => '\b',
                'f' => '\f',
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                'v' => '\v',
                '"' => '"',
                '?' => '?',
                '\'' => '\'',
                '\\' => '\\',
                _ => (char?)null
            };
            if (decodedCharacter is not null)
            {
                decoded.Append(decodedCharacter.Value);
                continue;
            }

            if (escapedCharacter == 'x')
            {
                var escapeValue = 0;
                var foundDigit = false;
                while (index + 1 < encoded.Length && HexValue(encoded[index + 1]) is var digit && digit >= 0)
                {
                    foundDigit = true;
                    escapeValue = ((escapeValue << 4) + digit) & 0xFFFF;
                    index++;
                }

                if (foundDigit)
                {
                    decoded.Append((char)escapeValue);
                }

                continue;
            }

            if (escapedCharacter is >= '0' and <= '7')
            {
                var escapeValue = escapedCharacter - '0';
                while (index + 1 < encoded.Length && encoded[index + 1] is >= '0' and <= '7')
                {
                    escapeValue = ((escapeValue << 3) + encoded[++index] - '0') & 0xFFFF;
                }

                decoded.Append((char)escapeValue);
            }
        }

        return decoded.ToString();
    }

    private static string DecodeByteArray(string payload, string context, string raw)
    {
        var bytes = new byte[payload.Length];
        for (var index = 0; index < payload.Length; index++)
        {
            if (payload[index] > byte.MaxValue)
            {
                throw Malformed(context, raw, "@ByteArray payload is not byte-valued");
            }

            bytes[index] = (byte)payload[index];
        }

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidOperationException(
                $"Malformed QSettings value for {context}: @ByteArray payload is not valid UTF-8: {raw}",
                exception);
        }
    }

    private static IReadOnlyList<int> FindUnescapedQuotes(string value)
    {
        var result = new List<int>();
        var escaped = false;
        for (var index = 0; index < value.Length; index++)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (value[index] == '\\')
            {
                escaped = true;
            }
            else if (value[index] == '"')
            {
                result.Add(index);
            }
        }

        return result;
    }

    private static InvalidOperationException Malformed(string context, string raw, string reason) =>
        new($"Malformed QSettings value for {context}: {reason}: {raw}");

    private static int HexValue(char value) => value switch
    {
        >= '0' and <= '9' => value - '0',
        >= 'a' and <= 'f' => value - 'a' + 10,
        >= 'A' and <= 'F' => value - 'A' + 10,
        _ => -1
    };
}
