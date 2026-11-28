using System;
using System.Collections.Generic;

namespace Blazium.Games.Tests
{
    // Values that crossed into PlayerPrefs. Unity 2021.3's managed SetString forwards the string
    // unchanged to native SetString (UnityCsReference 2021.3, Runtime/Export/PlayerPrefs/PlayerPrefs.bindings.cs
    // line 53). The 2021.3 PlayerPrefs documentation says that store is a local registry without encryption
    // (Windows HKCU, macOS plist, Android shared_prefs XML, Linux ~/.config/unity3d, iOS NSUserDefaults),
    // and that Unity also writes it on quit. A same-user reader of that store therefore sees these strings.
    internal static class PreferenceFile
    {
        internal struct Entry
        {
            public string Key;
            public string Value;
        }

        private static readonly List<Entry> written = new List<Entry>();

        internal static void Note(string key, string value)
        {
            Entry entry = new Entry();
            entry.Key = key ?? string.Empty;
            entry.Value = value ?? string.Empty;
            written.Add(entry);
        }

        internal static Entry[] Snapshot()
        {
            return written.ToArray();
        }

        internal static void Reset()
        {
            written.Clear();
        }
    }
}

namespace UnityEngine
{
    public class PlayerPrefsException : Exception
    {
        public PlayerPrefsException(string error) : base(error) { }
    }

    // Stand-in for the managed PlayerPrefs surface BlaziumPlayerPrefsStore calls.
    // SetString keeps the value as given: the 2021.3 binding does not transform it before native code,
    // and the engine documents the backing store as unencrypted.
    public static class PlayerPrefs
    {
        private static readonly Dictionary<string, string> memory = new Dictionary<string, string>();

        public static void SetString(string key, string value)
        {
            string stored = value ?? string.Empty;
            memory[key] = stored;
            Blazium.Games.Tests.PreferenceFile.Note(key, stored);
        }

        public static string GetString(string key, string defaultValue)
        {
            string found;
            if (key != null && memory.TryGetValue(key, out found))
            {
                return found;
            }
            return defaultValue ?? string.Empty;
        }

        public static string GetString(string key)
        {
            return GetString(key, string.Empty);
        }

        public static void DeleteKey(string key)
        {
            if (key != null)
            {
                memory.Remove(key);
            }
        }

        public static void Save()
        {
            // Native Sync flushes the values already accepted by SetString. The product calls this
            // immediately, and the 2021.3 docs also flush on quit, so the noted values are the file.
        }

        internal static void ResetForTests()
        {
            memory.Clear();
            Blazium.Games.Tests.PreferenceFile.Reset();
        }
    }

    public sealed class WaitForSecondsRealtime
    {
        public WaitForSecondsRealtime(float seconds)
        {
        }
    }
}

namespace UnityEngine.Networking
{
    public class DownloadHandler
    {
        public string text = string.Empty;
    }

    public sealed class DownloadHandlerBuffer : DownloadHandler
    {
    }

    public class UploadHandler
    {
    }

    public sealed class UploadHandlerRaw : UploadHandler
    {
        public UploadHandlerRaw(byte[] data)
        {
        }
    }

    public sealed class UnityWebRequest : IDisposable
    {
        public enum Result
        {
            Success,
            ProtocolError,
            ConnectionError,
            DataProcessingError,
        }

        public int timeout;
        public DownloadHandler downloadHandler;
        public UploadHandler uploadHandler;
        public Result result;
        public long responseCode;

        public UnityWebRequest(string url, string method)
        {
        }

        public void SetRequestHeader(string name, string value)
        {
        }

        public object SendWebRequest()
        {
            return null;
        }

        public string GetResponseHeader(string name)
        {
            return string.Empty;
        }

        public void Dispose()
        {
        }
    }
}
