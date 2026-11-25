#if UNITY_2021_3_OR_NEWER
using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Blazium.Games
{
    // Sends Scripted API requests with UnityWebRequest.
    public sealed class BlaziumUnityTransport : IBlaziumSapiTransport
    {
        // Seconds per request.
        public int TimeoutSeconds = 15;

        public IEnumerator Send(BlaziumSapiRequest request, BlaziumSapiResult result)
        {
            using (UnityWebRequest web = new UnityWebRequest(request.Url, request.Method))
            {
                web.timeout = TimeoutSeconds;
                web.downloadHandler = new DownloadHandlerBuffer();
                if (request.Method != "GET" && request.Body.Length > 0)
                {
                    web.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(request.Body));
                }
                foreach (var header in request.Headers)
                {
                    web.SetRequestHeader(header.Key, header.Value);
                }
                yield return web.SendWebRequest();
                bool reached = web.result == UnityWebRequest.Result.Success || web.result == UnityWebRequest.Result.ProtocolError;
                result.Status = reached ? (int)web.responseCode : 0;
                result.Body = reached && web.downloadHandler != null ? web.downloadHandler.text : string.Empty;
                result.RequestId = web.GetResponseHeader("x-request-id") ?? string.Empty;
                result.RetryAfter = web.GetResponseHeader("retry-after") ?? string.Empty;
            }
        }

        public IEnumerator Wait(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
        }
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    // A WebGL game runs in the web player and has no host. It asks the play origin that served it for
    // a token with POST /blazium/token, using the play cookie that origin set. The reply is a scoped
    // token for this game only.
    public sealed class BlaziumWebTokenSource : IBlaziumTokenSource
    {
        private readonly System.Collections.Generic.Dictionary<string, BlaziumTokenReply> cache = new System.Collections.Generic.Dictionary<string, BlaziumTokenReply>();
        private readonly System.Collections.Generic.Dictionary<string, float> until = new System.Collections.Generic.Dictionary<string, float>();

        public IEnumerator Get(string audience, BlaziumTokenReply reply)
        {
            float now = Time.realtimeSinceStartup;
            BlaziumTokenReply held;
            float expires;
            if (cache.TryGetValue(audience, out held) && until.TryGetValue(audience, out expires) && now < expires)
            {
                NativeTokenSource.Copy(held, reply);
                yield break;
            }
            Uri page = new Uri(Application.absoluteURL);
            string origin = page.GetLeftPart(UriPartial.Authority);
            using (UnityWebRequest web = new UnityWebRequest(origin + "/blazium/token", "POST"))
            {
                web.timeout = 15;
                web.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{\"audience\":\"" + audience + "\"}"));
                web.downloadHandler = new DownloadHandlerBuffer();
                web.SetRequestHeader("Content-Type", "application/json");
                yield return web.SendWebRequest();
                BlaziumTokenReply parsed = BlaziumTokenReply.Parse(web.downloadHandler != null ? web.downloadHandler.text : string.Empty);
                if (!parsed.Ok && string.IsNullOrEmpty(parsed.Code))
                {
                    parsed.Code = "http_" + web.responseCode;
                }
                NativeTokenSource.Copy(parsed, reply);
                if (parsed.Ok)
                {
                    int margin = parsed.ExpiresIn > 120 ? 60 : parsed.ExpiresIn / 2;
                    cache[audience] = parsed;
                    until[audience] = now + Math.Max(0, parsed.ExpiresIn - margin);
                }
            }
        }

        public void Forget()
        {
            cache.Clear();
            until.Clear();
        }
    }
#endif

    // A ready-made client: BlaziumUnitySapi.Create() uses UnityWebRequest and the right token source for the platform.
    public static class BlaziumUnitySapi
    {
        public static BlaziumSapi Create()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return new BlaziumSapi(new BlaziumUnityTransport(), new BlaziumWebTokenSource());
#else
            return new BlaziumSapi(new BlaziumUnityTransport());
#endif
        }
    }
}
#endif
