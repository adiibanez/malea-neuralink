using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Sensocto.Core
{
    /// <summary>
    /// Phoenix protocol message structure.
    /// Format: [join_ref, ref, topic, event, payload]
    /// </summary>
    public class PhoenixMessage
    {
        public string JoinRef { get; set; }
        public string Ref { get; set; }
        public string Topic { get; set; }
        public string Event { get; set; }
        public object Payload { get; set; }

        public PhoenixMessage() { }

        public PhoenixMessage(string topic, string evt, object payload, string @ref = null, string joinRef = null)
        {
            Topic = topic;
            Event = evt;
            Payload = payload;
            Ref = @ref;
            JoinRef = joinRef;
        }

        public bool IsReply => Event == PhoenixEvents.Reply;
        public bool IsError => Event == PhoenixEvents.Error;
        public bool IsClose => Event == PhoenixEvents.Close;
        public bool IsHeartbeat => Event == PhoenixEvents.Heartbeat;
    }

    /// <summary>
    /// Phoenix protocol event names.
    /// </summary>
    public static class PhoenixEvents
    {
        public const string Join = "phx_join";
        public const string Reply = "phx_reply";
        public const string Error = "phx_error";
        public const string Close = "phx_close";
        public const string Leave = "phx_leave";
        public const string Heartbeat = "heartbeat";
    }

    /// <summary>
    /// Reply status from Phoenix server.
    /// </summary>
    public class PhoenixReply
    {
        public string Status { get; set; }
        public object Response { get; set; }

        public bool IsOk => Status == "ok";
        public bool IsError => Status == "error";

        public static PhoenixReply FromPayload(object payload)
        {
            if (payload is Dictionary<string, object> dict)
            {
                return new PhoenixReply
                {
                    Status = dict.TryGetValue("status", out var s) ? s?.ToString() : null,
                    Response = dict.TryGetValue("response", out var r) ? r : null
                };
            }
            return new PhoenixReply { Status = "error", Response = payload };
        }
    }

    /// <summary>
    /// JSON serializer for Phoenix protocol messages.
    /// Implements a lightweight JSON parser/serializer to avoid external dependencies.
    /// </summary>
    public static class PhoenixSerializer
    {
        public static string Encode(PhoenixMessage message)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            sb.Append(EncodeValue(message.JoinRef));
            sb.Append(',');
            sb.Append(EncodeValue(message.Ref));
            sb.Append(',');
            sb.Append(EncodeValue(message.Topic));
            sb.Append(',');
            sb.Append(EncodeValue(message.Event));
            sb.Append(',');
            sb.Append(EncodeValue(message.Payload));
            sb.Append(']');
            return sb.ToString();
        }

        public static PhoenixMessage Decode(string json)
        {
            var array = ParseJsonArray(json);
            if (array == null || array.Count < 5)
                return null;

            return new PhoenixMessage
            {
                JoinRef = array[0]?.ToString(),
                Ref = array[1]?.ToString(),
                Topic = array[2]?.ToString(),
                Event = array[3]?.ToString(),
                Payload = array[4]
            };
        }

        private static string EncodeValue(object value)
        {
            if (value == null)
                return "null";

            switch (value)
            {
                case string s:
                    return EncodeString(s);
                case bool b:
                    return b ? "true" : "false";
                case int i:
                    return i.ToString(CultureInfo.InvariantCulture);
                case long l:
                    return l.ToString(CultureInfo.InvariantCulture);
                case float f:
                    return f.ToString(CultureInfo.InvariantCulture);
                case double d:
                    return d.ToString(CultureInfo.InvariantCulture);
                case Dictionary<string, object> dict:
                    return EncodeDictionary(dict);
                case IEnumerable<object> list:
                    return EncodeArray(list);
                case Array arr:
                    return EncodeArray(ToObjectEnumerable(arr));
                default:
                    return EncodeString(value.ToString());
            }
        }

        private static IEnumerable<object> ToObjectEnumerable(Array arr)
        {
            foreach (var item in arr)
                yield return item;
        }

        private static string EncodeString(string s)
        {
            var sb = new StringBuilder();
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32)
                            sb.AppendFormat("\\u{0:X4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static string EncodeDictionary(Dictionary<string, object> dict)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            bool first = true;
            foreach (var kvp in dict)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(EncodeString(kvp.Key));
                sb.Append(':');
                sb.Append(EncodeValue(kvp.Value));
            }
            sb.Append('}');
            return sb.ToString();
        }

        private static string EncodeArray(IEnumerable<object> arr)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            bool first = true;
            foreach (var item in arr)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(EncodeValue(item));
            }
            sb.Append(']');
            return sb.ToString();
        }

        // Simple JSON parser
        private static List<object> ParseJsonArray(string json)
        {
            int index = 0;
            return ParseArray(json, ref index);
        }

        private static object ParseValue(string json, ref int index)
        {
            SkipWhitespace(json, ref index);
            if (index >= json.Length) return null;

            char c = json[index];
            switch (c)
            {
                case '"': return ParseString(json, ref index);
                case '{': return ParseObject(json, ref index);
                case '[': return ParseArray(json, ref index);
                case 't':
                case 'f': return ParseBool(json, ref index);
                case 'n': return ParseNull(json, ref index);
                default:
                    if (c == '-' || char.IsDigit(c))
                        return ParseNumber(json, ref index);
                    return null;
            }
        }

        private static void SkipWhitespace(string json, ref int index)
        {
            while (index < json.Length && char.IsWhiteSpace(json[index]))
                index++;
        }

        private static string ParseString(string json, ref int index)
        {
            index++; // skip opening quote
            var sb = new StringBuilder();
            while (index < json.Length)
            {
                char c = json[index++];
                if (c == '"') break;
                if (c == '\\' && index < json.Length)
                {
                    char next = json[index++];
                    switch (next)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (index + 4 <= json.Length)
                            {
                                string hex = json.Substring(index, 4);
                                sb.Append((char)int.Parse(hex, NumberStyles.HexNumber));
                                index += 4;
                            }
                            break;
                        default: sb.Append(next); break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static Dictionary<string, object> ParseObject(string json, ref int index)
        {
            var dict = new Dictionary<string, object>();
            index++; // skip {
            SkipWhitespace(json, ref index);

            while (index < json.Length && json[index] != '}')
            {
                SkipWhitespace(json, ref index);
                if (json[index] == '}') break;

                string key = ParseString(json, ref index);
                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ':') index++;
                SkipWhitespace(json, ref index);
                object value = ParseValue(json, ref index);
                dict[key] = value;

                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ',') index++;
            }

            if (index < json.Length) index++; // skip }
            return dict;
        }

        private static List<object> ParseArray(string json, ref int index)
        {
            var list = new List<object>();
            index++; // skip [
            SkipWhitespace(json, ref index);

            while (index < json.Length && json[index] != ']')
            {
                SkipWhitespace(json, ref index);
                if (json[index] == ']') break;

                object value = ParseValue(json, ref index);
                list.Add(value);

                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ',') index++;
            }

            if (index < json.Length) index++; // skip ]
            return list;
        }

        private static object ParseNumber(string json, ref int index)
        {
            int start = index;
            if (json[index] == '-') index++;
            while (index < json.Length && (char.IsDigit(json[index]) || json[index] == '.' || json[index] == 'e' || json[index] == 'E' || json[index] == '+' || json[index] == '-'))
                index++;

            string numStr = json.Substring(start, index - start);
            if (numStr.Contains(".") || numStr.Contains("e") || numStr.Contains("E"))
                return double.Parse(numStr, CultureInfo.InvariantCulture);

            if (long.TryParse(numStr, out long l))
                return l;
            return double.Parse(numStr, CultureInfo.InvariantCulture);
        }

        private static bool ParseBool(string json, ref int index)
        {
            if (json.Substring(index).StartsWith("true"))
            {
                index += 4;
                return true;
            }
            index += 5;
            return false;
        }

        private static object ParseNull(string json, ref int index)
        {
            index += 4;
            return null;
        }
    }
}
