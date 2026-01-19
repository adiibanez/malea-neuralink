using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Sensocto.SDK
{
    /// <summary>
    /// Simple JSON serializer/deserializer for Unity.
    /// Provides basic JSON support without external dependencies.
    /// For production use, consider using Unity's JsonUtility or Newtonsoft.Json.
    /// </summary>
    public static class JsonSerializer
    {
        /// <summary>
        /// Serializes an object to JSON string.
        /// </summary>
        public static string Serialize(object obj)
        {
            if (obj == null) return "null";

            var sb = new StringBuilder();
            SerializeValue(obj, sb);
            return sb.ToString();
        }

        /// <summary>
        /// Deserializes a JSON string to a Dictionary.
        /// </summary>
        public static Dictionary<string, object> Deserialize<T>(string json) where T : Dictionary<string, object>
        {
            if (string.IsNullOrEmpty(json)) return new Dictionary<string, object>();

            var index = 0;
            return ParseObject(json, ref index);
        }

        private static void SerializeValue(object value, StringBuilder sb)
        {
            if (value == null)
            {
                sb.Append("null");
            }
            else if (value is string str)
            {
                SerializeString(str, sb);
            }
            else if (value is bool b)
            {
                sb.Append(b ? "true" : "false");
            }
            else if (value is int || value is long || value is short || value is byte)
            {
                sb.Append(value);
            }
            else if (value is float f)
            {
                sb.Append(f.ToString(CultureInfo.InvariantCulture));
            }
            else if (value is double d)
            {
                sb.Append(d.ToString(CultureInfo.InvariantCulture));
            }
            else if (value is decimal dec)
            {
                sb.Append(dec.ToString(CultureInfo.InvariantCulture));
            }
            else if (value is IDictionary dict)
            {
                SerializeDictionary(dict, sb);
            }
            else if (value is IList list)
            {
                SerializeList(list, sb);
            }
            else
            {
                // Fallback: serialize as string
                SerializeString(value.ToString(), sb);
            }
        }

        private static void SerializeString(string str, StringBuilder sb)
        {
            sb.Append('"');
            foreach (var c in str)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
        }

        private static void SerializeDictionary(IDictionary dict, StringBuilder sb)
        {
            sb.Append('{');
            var first = true;
            foreach (DictionaryEntry entry in dict)
            {
                if (!first) sb.Append(',');
                first = false;

                SerializeString(entry.Key.ToString(), sb);
                sb.Append(':');
                SerializeValue(entry.Value, sb);
            }
            sb.Append('}');
        }

        private static void SerializeList(IList list, StringBuilder sb)
        {
            sb.Append('[');
            var first = true;
            foreach (var item in list)
            {
                if (!first) sb.Append(',');
                first = false;
                SerializeValue(item, sb);
            }
            sb.Append(']');
        }

        // Parsing methods
        private static Dictionary<string, object> ParseObject(string json, ref int index)
        {
            var result = new Dictionary<string, object>();

            SkipWhitespace(json, ref index);

            if (json[index] != '{')
                throw new FormatException("Expected '{'");

            index++; // Skip '{'

            SkipWhitespace(json, ref index);

            if (json[index] == '}')
            {
                index++;
                return result;
            }

            while (true)
            {
                SkipWhitespace(json, ref index);

                var key = ParseString(json, ref index);

                SkipWhitespace(json, ref index);

                if (json[index] != ':')
                    throw new FormatException("Expected ':'");

                index++; // Skip ':'

                SkipWhitespace(json, ref index);

                var value = ParseValue(json, ref index);

                result[key] = value;

                SkipWhitespace(json, ref index);

                if (json[index] == '}')
                {
                    index++;
                    break;
                }

                if (json[index] != ',')
                    throw new FormatException("Expected ',' or '}'");

                index++; // Skip ','
            }

            return result;
        }

        private static List<object> ParseArray(string json, ref int index)
        {
            var result = new List<object>();

            index++; // Skip '['

            SkipWhitespace(json, ref index);

            if (json[index] == ']')
            {
                index++;
                return result;
            }

            while (true)
            {
                SkipWhitespace(json, ref index);

                var value = ParseValue(json, ref index);
                result.Add(value);

                SkipWhitespace(json, ref index);

                if (json[index] == ']')
                {
                    index++;
                    break;
                }

                if (json[index] != ',')
                    throw new FormatException("Expected ',' or ']'");

                index++; // Skip ','
            }

            return result;
        }

        private static object ParseValue(string json, ref int index)
        {
            SkipWhitespace(json, ref index);

            var c = json[index];

            if (c == '"')
            {
                return ParseString(json, ref index);
            }
            else if (c == '{')
            {
                return ParseObject(json, ref index);
            }
            else if (c == '[')
            {
                return ParseArray(json, ref index);
            }
            else if (c == 't' || c == 'f')
            {
                return ParseBool(json, ref index);
            }
            else if (c == 'n')
            {
                return ParseNull(json, ref index);
            }
            else if (c == '-' || char.IsDigit(c))
            {
                return ParseNumber(json, ref index);
            }

            throw new FormatException($"Unexpected character: {c}");
        }

        private static string ParseString(string json, ref int index)
        {
            if (json[index] != '"')
                throw new FormatException("Expected '\"'");

            index++; // Skip opening quote

            var sb = new StringBuilder();

            while (index < json.Length)
            {
                var c = json[index];

                if (c == '"')
                {
                    index++;
                    return sb.ToString();
                }

                if (c == '\\')
                {
                    index++;
                    if (index >= json.Length)
                        throw new FormatException("Unexpected end of string");

                    var escaped = json[index];
                    switch (escaped)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (index + 4 >= json.Length)
                                throw new FormatException("Invalid unicode escape");
                            var hex = json.Substring(index + 1, 4);
                            sb.Append((char)int.Parse(hex, NumberStyles.HexNumber));
                            index += 4;
                            break;
                        default:
                            throw new FormatException($"Invalid escape sequence: \\{escaped}");
                    }
                }
                else
                {
                    sb.Append(c);
                }

                index++;
            }

            throw new FormatException("Unterminated string");
        }

        private static object ParseNumber(string json, ref int index)
        {
            var start = index;

            if (json[index] == '-')
                index++;

            while (index < json.Length && char.IsDigit(json[index]))
                index++;

            var isFloat = false;

            if (index < json.Length && json[index] == '.')
            {
                isFloat = true;
                index++;
                while (index < json.Length && char.IsDigit(json[index]))
                    index++;
            }

            if (index < json.Length && (json[index] == 'e' || json[index] == 'E'))
            {
                isFloat = true;
                index++;
                if (index < json.Length && (json[index] == '+' || json[index] == '-'))
                    index++;
                while (index < json.Length && char.IsDigit(json[index]))
                    index++;
            }

            var numStr = json.Substring(start, index - start);

            if (isFloat)
            {
                return double.Parse(numStr, CultureInfo.InvariantCulture);
            }
            else
            {
                if (long.TryParse(numStr, out var longVal))
                {
                    if (longVal >= int.MinValue && longVal <= int.MaxValue)
                        return (int)longVal;
                    return longVal;
                }
                return double.Parse(numStr, CultureInfo.InvariantCulture);
            }
        }

        private static bool ParseBool(string json, ref int index)
        {
            if (json.Substring(index, 4) == "true")
            {
                index += 4;
                return true;
            }
            else if (json.Substring(index, 5) == "false")
            {
                index += 5;
                return false;
            }

            throw new FormatException("Expected 'true' or 'false'");
        }

        private static object ParseNull(string json, ref int index)
        {
            if (json.Substring(index, 4) == "null")
            {
                index += 4;
                return null;
            }

            throw new FormatException("Expected 'null'");
        }

        private static void SkipWhitespace(string json, ref int index)
        {
            while (index < json.Length && char.IsWhiteSpace(json[index]))
                index++;
        }
    }
}
