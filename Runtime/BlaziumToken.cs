using System;
using System.Text;

namespace Blazium.Games
{
    // The reply of BlaziumApi.RequestToken, parsed. The token is a secret: never log it.
    public sealed class BlaziumTokenReply
    {
        public bool Ok;
        public string Token = string.Empty;
        public string Audience = string.Empty;
        public string GameUid = string.Empty;
        public string ExpiresAt = string.Empty;
        public int ExpiresIn;
        public string Code = string.Empty;
        public string Error = string.Empty;

        public static BlaziumTokenReply Parse(string json)
        {
            BlaziumTokenReply reply = new BlaziumTokenReply();
            if (string.IsNullOrEmpty(json))
            {
                reply.Code = "no_host";
                reply.Error = "Blazium is not initialized";
                return reply;
            }
            string ok = BlaziumJson.Field(json, "ok");
            reply.Token = BlaziumJson.Field(json, "token");
            reply.Audience = BlaziumJson.Field(json, "audience");
            reply.GameUid = BlaziumJson.Field(json, "game_uid");
            reply.ExpiresAt = BlaziumJson.Field(json, "expires_at");
            int.TryParse(BlaziumJson.Field(json, "expires_in"), out reply.ExpiresIn);
            reply.Code = BlaziumJson.Field(json, "code");
            reply.Error = BlaziumJson.Field(json, "error");
            reply.Ok = ok == "true" && reply.Token.Length > 0;
            if (!reply.Ok)
            {
                reply.Token = string.Empty;
                if (reply.Code.Length == 0)
                {
                    reply.Code = "refused";
                }
            }
            return reply;
        }

        // Never prints the token.
        public override string ToString()
        {
            return Ok ? "token for " + Audience + ", " + ExpiresIn + "s" : Code + ": " + Error;
        }
    }

    // Just enough JSON to read the flat replies of the library and the Scripted API envelope.
    internal static class BlaziumJson
    {
        // Returns the raw text of a top-level-looking "key": value, unquoted and unescaped for strings.
        internal static string Field(string json, string key)
        {
            if (string.IsNullOrEmpty(json))
            {
                return string.Empty;
            }
            string pattern = "\"" + key + "\"";
            int at = 0;
            while (true)
            {
                at = json.IndexOf(pattern, at, StringComparison.Ordinal);
                if (at < 0)
                {
                    return string.Empty;
                }
                int i = at + pattern.Length;
                while (i < json.Length && json[i] == ' ')
                {
                    i++;
                }
                if (i >= json.Length || json[i] != ':')
                {
                    at += pattern.Length;
                    continue;
                }
                i++;
                while (i < json.Length && json[i] == ' ')
                {
                    i++;
                }
                if (i >= json.Length)
                {
                    return string.Empty;
                }
                if (json[i] == '"')
                {
                    return ReadString(json, i + 1);
                }
                int end = i;
                while (end < json.Length && json[end] != ',' && json[end] != '}' && json[end] != ']')
                {
                    end++;
                }
                return json.Substring(i, end - i).Trim();
            }
        }

        private static string ReadString(string json, int start)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = start; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"')
                {
                    return sb.ToString();
                }
                if (c != '\\' || i + 1 >= json.Length)
                {
                    sb.Append(c);
                    continue;
                }
                i++;
                switch (json[i])
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        int code;
                        if (i + 4 < json.Length && int.TryParse(json.Substring(i + 1, 4), System.Globalization.NumberStyles.HexNumber, null, out code))
                        {
                            sb.Append((char)code);
                            i += 4;
                        }
                        break;
                    default: sb.Append(json[i]); break;
                }
            }
            return sb.ToString();
        }
    }
}
