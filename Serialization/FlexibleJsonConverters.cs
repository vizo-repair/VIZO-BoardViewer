namespace VIZO.BoardViewer.Serialization;

using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Accepts JSON number / numeric string / null so autosave after a move
/// does not fail on pin: "1A" or minX: null from the client.
/// </summary>
public sealed class FlexibleIntJsonConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return 0;
        if (reader.TokenType == JsonTokenType.Number)
        {
            if (reader.TryGetInt32(out int n)) return n;
            if (reader.TryGetDouble(out double d) && !double.IsNaN(d) && !double.IsInfinity(d))
                return (int)d;
            return 0;
        }
        if (reader.TokenType == JsonTokenType.String)
            return ParseInt(reader.GetString());
        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);

    internal static int ParseInt(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0;
        string s = raw.Trim();
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
            return n;
        int i = 0;
        while (i < s.Length && char.IsDigit(s[i])) i++;
        if (i > 0 && int.TryParse(s[..i], NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
            return n;
        return 0;
    }
}

public sealed class FlexibleDoubleJsonConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return 0;
        if (reader.TokenType == JsonTokenType.Number)
            return reader.TryGetDouble(out double d) && !double.IsNaN(d) && !double.IsInfinity(d) ? d : 0;
        if (reader.TokenType == JsonTokenType.String)
        {
            string? s = reader.GetString();
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double d)
                && !double.IsNaN(d) && !double.IsInfinity(d))
                return d;
            return 0;
        }
        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);
}
