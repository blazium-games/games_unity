#if UNITY_2021_3_OR_NEWER
using System;
using System.Collections;
using System.Runtime.InteropServices;
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
                web.timeout = request.TimeoutSeconds > 0 ? request.TimeoutSeconds : TimeoutSeconds;
                web.downloadHandler = new DownloadHandlerBuffer();
                if (request.Method != "GET" && request.Method != "DELETE" && request.Body.Length > 0)
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

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    // Windows DPAPI for the current user: the sealed bytes open only for this Windows account on this
    // machine, so a copied registry hive or backup is useless elsewhere.
    public sealed class BlaziumDpapiProtector : IBlaziumSecretProtector
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int Size;
            public IntPtr Data;
        }

        private const int UiForbidden = 0x1;

        [DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CryptProtectData(ref DataBlob dataIn, IntPtr description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob dataOut);

        [DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob dataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

        private readonly byte[] entropy;

        public BlaziumDpapiProtector(string purpose)
        {
            entropy = Encoding.UTF8.GetBytes(string.IsNullOrEmpty(purpose) ? "blazium.session" : purpose);
        }

        public byte[] Protect(byte[] plain)
        {
            return Run(plain, true);
        }

        public byte[] Unprotect(byte[] sealedValue)
        {
            return Run(sealedValue, false);
        }

        private byte[] Run(byte[] input, bool protect)
        {
            if (input == null || input.Length == 0)
            {
                return null;
            }
            GCHandle inputHandle = GCHandle.Alloc(input, GCHandleType.Pinned);
            GCHandle entropyHandle = GCHandle.Alloc(entropy, GCHandleType.Pinned);
            DataBlob output = new DataBlob();
            try
            {
                DataBlob source = new DataBlob { Size = input.Length, Data = inputHandle.AddrOfPinnedObject() };
                DataBlob salt = new DataBlob { Size = entropy.Length, Data = entropyHandle.AddrOfPinnedObject() };
                bool ok = protect
                    ? CryptProtectData(ref source, IntPtr.Zero, ref salt, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output)
                    : CryptUnprotectData(ref source, IntPtr.Zero, ref salt, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output);
                if (!ok || output.Data == IntPtr.Zero || output.Size <= 0)
                {
                    return null;
                }
                byte[] result = new byte[output.Size];
                Marshal.Copy(output.Data, result, 0, output.Size);
                return result;
            }
            catch (DllNotFoundException)
            {
                return null;
            }
            catch (EntryPointNotFoundException)
            {
                return null;
            }
            finally
            {
                if (output.Data != IntPtr.Zero)
                {
                    Marshal.Copy(new byte[output.Size], 0, output.Data, output.Size);
                    LocalFree(output.Data);
                }
                inputHandle.Free();
                entropyHandle.Free();
            }
        }
    }
#endif

    public static class BlaziumSecretProtectors
    {
        // The OS secret store for this player, or null when the package has none for the platform.
        // On a phone or console pass your own (Keystore, Keychain, protected save data).
        public static IBlaziumSecretProtector ForPlatform(string gameUid)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            return new BlaziumDpapiProtector("blazium.session." + gameUid);
#else
            return null;
#endif
        }
    }

    // Keeps the session in PlayerPrefs, sealed by an IBlaziumSecretProtector. PlayerPrefs itself is NOT
    // encrypted (the registry on Windows, a plist on macOS and iOS, shared preferences on Android), so
    // the raw session is never written there. With no protector the session stays in memory only and
    // the player signs in again on the next run. An unsealed entry left by an older version is erased.
    public sealed class BlaziumPlayerPrefsStore : IBlaziumSessionStore
    {
        private readonly string key;
        private readonly string plainKey;
        private readonly IBlaziumSecretProtector protector;

        public BlaziumPlayerPrefsStore(string gameUid) : this(gameUid, BlaziumSecretProtectors.ForPlatform(gameUid))
        {
        }

        public BlaziumPlayerPrefsStore(string gameUid, IBlaziumSecretProtector protector)
        {
            key = "blazium.session.sealed." + gameUid;
            plainKey = "blazium.session." + gameUid;
            this.protector = protector;
        }

        // False when nothing is kept between runs on this platform.
        public bool Persists { get { return protector != null; } }

        public string Load()
        {
            string legacy = PlayerPrefs.GetString(plainKey, string.Empty);
            if (legacy.Length > 0)
            {
                Save(legacy);
                return legacy;
            }
            if (protector == null)
            {
                return string.Empty;
            }
            string stored = PlayerPrefs.GetString(key, string.Empty);
            if (stored.Length == 0)
            {
                return string.Empty;
            }
            byte[] opened = null;
            try
            {
                opened = protector.Unprotect(Convert.FromBase64String(stored));
            }
            catch (Exception)
            {
            }
            if (opened == null || opened.Length == 0)
            {
                Clear();
                return string.Empty;
            }
            return Encoding.UTF8.GetString(opened);
        }

        public void Save(string session)
        {
            PlayerPrefs.DeleteKey(plainKey);
            byte[] sealedValue = null;
            if (protector != null && !string.IsNullOrEmpty(session))
            {
                try
                {
                    sealedValue = protector.Protect(Encoding.UTF8.GetBytes(session));
                }
                catch (Exception)
                {
                    sealedValue = null;
                }
            }
            if (sealedValue == null || sealedValue.Length == 0)
            {
                PlayerPrefs.DeleteKey(key);
            }
            else
            {
                PlayerPrefs.SetString(key, Convert.ToBase64String(sealedValue));
            }
            PlayerPrefs.Save();
        }

        public void Clear()
        {
            PlayerPrefs.DeleteKey(plainKey);
            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
        }
    }

    // A ready-made player session: UnityWebRequest for HTTP and a sealed PlayerPrefs store (or your own
    // store) for the saved sign-in. No native library and no launcher are needed.
    public static class BlaziumUnitySignIn
    {
        public static BlaziumPlayerSession Create(string gameUid, IBlaziumSessionStore store = null)
        {
            return new BlaziumPlayerSession(new BlaziumUnityTransport(), gameUid, store ?? new BlaziumPlayerPrefsStore(gameUid));
        }

        // The Scripted API client for a signed-in player session.
        public static BlaziumSapi CreateSapi(BlaziumPlayerSession session)
        {
            BlaziumSapi sapi = new BlaziumSapi(new BlaziumUnityTransport(), session);
            sapi.GameUid = session.GameUid;
            return sapi;
        }
    }

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
