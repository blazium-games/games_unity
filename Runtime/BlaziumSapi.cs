using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Blazium.Games
{
    // One Scripted API request, ready to send. The Authorization header carries a scoped token.
    public sealed class BlaziumSapiRequest
    {
        public string Method = "POST";
        public string Url = string.Empty;
        public string Body = string.Empty;
        public readonly Dictionary<string, string> Headers = new Dictionary<string, string>();
    }

    // What the transport got back. Status 0 means the request could not be sent.
    public sealed class BlaziumSapiResult
    {
        public bool Success;
        public int Status;
        public string Body = string.Empty;
        public string RequestId = string.Empty;
        public string RetryAfter = string.Empty;
        // Set when the call failed. Key is the platform error key ("rate_limited", "http_404") or a client key.
        public string ErrorKey = string.Empty;
        public string Message = string.Empty;
    }

    // Sends HTTP and waits. Unity supplies BlaziumUnityTransport; a test supplies its own.
    public interface IBlaziumSapiTransport
    {
        IEnumerator Send(BlaziumSapiRequest request, BlaziumSapiResult result);
        IEnumerator Wait(float seconds);
    }

    // Gets a token for an audience. Native builds ask the host through the library; WebGL asks
    // the play origin's POST /blazium/token. A source never returns a login token.
    public interface IBlaziumTokenSource
    {
        IEnumerator Get(string audience, BlaziumTokenReply reply);
        void Forget();
    }

    internal sealed class NativeTokenSource : IBlaziumTokenSource
    {
        public IEnumerator Get(string audience, BlaziumTokenReply reply)
        {
            BlaziumTokenReply parsed = BlaziumTokenReply.Parse(BlaziumApi.RequestToken(audience));
            Copy(parsed, reply);
            yield break;
        }

        public void Forget()
        {
            BlaziumApi.ForgetTokens();
        }

        internal static void Copy(BlaziumTokenReply from, BlaziumTokenReply to)
        {
            to.Ok = from.Ok;
            to.Token = from.Token;
            to.Audience = from.Audience;
            to.GameUid = from.GameUid;
            to.ExpiresAt = from.ExpiresAt;
            to.ExpiresIn = from.ExpiresIn;
            to.Code = from.Code;
            to.Error = from.Error;
        }
    }

    // Calls this game's Scripted API at https://sapi.blazium.online/v1/{game_uid}/{logic}.
    // Signed-in calls use a short-lived token the host requested for this game; logic marked
    // auth = anonymous runs with Anonymous = true and no token. Use it from a coroutine:
    //
    //   var result = new BlaziumSapiResult();
    //   yield return sapi.Call("score_submit", "POST", "{\"score\":10}", result);
    public sealed class BlaziumSapi
    {
        public const string DefaultBase = "https://sapi.blazium.online";
        public const int MaxBodyBytes = 64 * 1024;

        public string BaseUrl = DefaultBase;
        // Sent as X-Blazium-Channel.
        public string Channel = "stable";
        // Tries after a 429 or 503 before the call fails.
        public int MaxRetries = 3;
        public string GameUid = string.Empty;

        private readonly IBlaziumSapiTransport transport;
        private readonly IBlaziumTokenSource tokens;

        public BlaziumSapi(IBlaziumSapiTransport transport, IBlaziumTokenSource tokens = null)
        {
            if (transport == null)
            {
                throw new ArgumentNullException("transport");
            }
            this.transport = transport;
            this.tokens = tokens ?? new NativeTokenSource();
        }

        public static bool ValidLogic(string logic)
        {
            if (string.IsNullOrEmpty(logic) || logic.Length > 64)
            {
                return false;
            }
            foreach (char c in logic)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
                if (!ok)
                {
                    return false;
                }
            }
            return true;
        }

        public static string NewIdempotencyKey()
        {
            return Guid.NewGuid().ToString("N");
        }

        // Builds the request without a token or the Idempotency-Key header's value being fixed by the caller.
        public BlaziumSapiRequest Build(string logic, string method, string body, string channel, string idempotencyKey, out string error)
        {
            error = string.Empty;
            if (!ValidLogic(logic))
            {
                error = "logic names are lowercase letters, digits, _ and -";
                return null;
            }
            string uid = string.IsNullOrEmpty(GameUid) ? BlaziumUser.GameUid() : GameUid;
            if (string.IsNullOrEmpty(uid))
            {
                error = "the game uid is not known; initialize Blazium or set GameUid";
                return null;
            }
            string verb = string.IsNullOrEmpty(method) ? "POST" : method.ToUpperInvariant();
            if (verb != "GET" && verb != "POST" && verb != "PUT" && verb != "PATCH" && verb != "DELETE")
            {
                error = "method must be GET, POST, PUT, PATCH or DELETE";
                return null;
            }
            BlaziumSapiRequest request = new BlaziumSapiRequest();
            request.Method = verb;
            request.Url = BaseUrl.TrimEnd('/') + "/v1/" + Uri.EscapeDataString(uid) + "/" + logic;
            if (verb == "GET")
            {
                // A GET sends its arguments as the query string. body is a query string without the "?".
                if (!string.IsNullOrEmpty(body))
                {
                    request.Url += "?" + body.TrimStart('?');
                }
            }
            else
            {
                request.Body = string.IsNullOrEmpty(body) ? "{}" : body;
                if (Encoding.UTF8.GetByteCount(request.Body) > MaxBodyBytes)
                {
                    error = "the request body is larger than 64 KiB";
                    return null;
                }
            }
            request.Headers["Content-Type"] = "application/json";
            request.Headers["Accept"] = "application/json";
            request.Headers["X-Blazium-Channel"] = string.IsNullOrEmpty(channel) ? (string.IsNullOrEmpty(Channel) ? "stable" : Channel.ToLowerInvariant()) : channel.ToLowerInvariant();
            if (verb != "GET")
            {
                // Retries reuse the header, so a repeated call applies once.
                request.Headers["Idempotency-Key"] = string.IsNullOrEmpty(idempotencyKey) ? NewIdempotencyKey() : idempotencyKey;
            }
            return request;
        }

        public IEnumerator Call(string logic, string method, string body, BlaziumSapiResult result, bool anonymous = false, string idempotencyKey = null, string channel = null)
        {
            string error;
            BlaziumSapiRequest request = Build(logic, method, body, channel, idempotencyKey, out error);
            if (request == null)
            {
                Fail(result, 0, "bad_request", error);
                yield break;
            }
            bool refreshedAfter401 = false;
            int attempt = 0;
            while (true)
            {
                request.Headers.Remove("Authorization");
                if (!anonymous)
                {
                    BlaziumTokenReply token = new BlaziumTokenReply();
                    yield return tokens.Get("sapi", token);
                    if (!token.Ok)
                    {
                        Fail(result, 0, string.IsNullOrEmpty(token.Code) ? "not_logged_on" : token.Code, string.IsNullOrEmpty(token.Error) ? "could not get a Scripted API token" : token.Error);
                        yield break;
                    }
                    request.Headers["Authorization"] = "Bearer " + token.Token;
                }
                Reset(result);
                yield return transport.Send(request, result);
                if (result.Status == 401 && !anonymous && !refreshedAfter401)
                {
                    // The token may have been revoked or aged out early. Ask once more.
                    refreshedAfter401 = true;
                    tokens.Forget();
                    continue;
                }
                bool retryable = result.Status == 0 || result.Status == 429 || result.Status == 503;
                if (!retryable || attempt >= MaxRetries)
                {
                    break;
                }
                yield return transport.Wait(RetryDelay(attempt, result.RetryAfter));
                attempt++;
            }
            if (result.Status >= 200 && result.Status < 300)
            {
                result.Success = true;
                yield break;
            }
            result.Success = false;
            if (result.Status == 0)
            {
                result.ErrorKey = "service_unavailable";
                result.Message = "the Scripted API could not be reached";
                yield break;
            }
            string key = BlaziumJson.Field(result.Body, "key");
            string message = BlaziumJson.Field(result.Body, "message");
            result.ErrorKey = key.Length > 0 ? key : "http_" + result.Status;
            result.Message = message.Length > 0 ? message : "the call was refused";
        }

        public static float RetryDelay(int attempt, string retryAfter)
        {
            float wait;
            if (!float.TryParse(retryAfter, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out wait) || wait <= 0f)
            {
                wait = 0.5f * (float)Math.Pow(2.0, attempt);
            }
            return wait > 8f ? 8f : wait;
        }

        private static void Reset(BlaziumSapiResult result)
        {
            result.Success = false;
            result.Status = 0;
            result.Body = string.Empty;
            result.RequestId = string.Empty;
            result.RetryAfter = string.Empty;
            result.ErrorKey = string.Empty;
            result.Message = string.Empty;
        }

        private static void Fail(BlaziumSapiResult result, int status, string key, string message)
        {
            Reset(result);
            result.Status = status;
            result.ErrorKey = key;
            result.Message = message;
        }
    }
}
