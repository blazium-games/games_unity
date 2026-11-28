using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Blazium.Games;
using UnityEngine;

namespace Blazium.Games.Tests
{
    // Records the requests BlaziumPlayerSession builds. This is the package's own transport seam;
    // it does not open a socket.
    internal sealed class HeaderTransport : IBlaziumSapiTransport
    {
        public readonly List<BlaziumSapiRequest> Sent = new List<BlaziumSapiRequest>();
        public string RefreshToken = string.Empty;

        public IEnumerator Send(BlaziumSapiRequest request, BlaziumSapiResult result)
        {
            BlaziumSapiRequest copy = new BlaziumSapiRequest();
            copy.Method = request.Method;
            copy.Url = request.Url;
            copy.Body = request.Body;
            foreach (KeyValuePair<string, string> header in request.Headers)
            {
                copy.Headers[header.Key] = header.Value;
            }
            Sent.Add(copy);
            result.Status = 200;
            result.Body = "{}";
            if (request.Url.IndexOf("/public/token/refresh", StringComparison.Ordinal) >= 0)
            {
                result.Body = "{\"token\":\"" + RefreshToken + "\"}";
            }
            else if (request.Url.IndexOf("/tokens", StringComparison.Ordinal) >= 0)
            {
                result.Body = "{\"data\":{\"token\":\"scoped-placeholder\",\"expires_in\":900,\"expires_at\":\"2099-01-01T00:00:00Z\"}}";
            }
            else if (request.Url.IndexOf("/private/irc/token", StringComparison.Ordinal) >= 0)
            {
                result.Body = "{\"username\":\"ada\",\"token\":\"chat-placeholder\",\"host\":\"irc.example\",\"port\":6697,\"tls\":true}";
            }
            else if (request.Url.IndexOf("/private/account", StringComparison.Ordinal) >= 0)
            {
                result.Body = "{\"username\":\"ada\",\"display_name\":\"Ada\"}";
            }
            else if (request.Url.IndexOf("/private/logout", StringComparison.Ordinal) >= 0)
            {
                result.Body = "{\"ok\":true}";
            }
            yield break;
        }

        public IEnumerator Wait(float seconds)
        {
            yield break;
        }
    }

    // The session must not be recoverable from the preference values PlayerPrefs accepted.
    // These checks fail while BlaziumPlayerPrefsStore writes the raw session.
    internal static class SessionStoreTests
    {
        private const long Now = 1700000000;

        public static void Run()
        {
            DefaultCreate();
            SignIn();
            Refresh();
            UseExplicitStore();
            DirectSave();
        }

        // BlaziumUnitySignIn.Create with no store is the call the player sign-in sample makes.
        private static void DefaultCreate()
        {
            PlayerPrefs.ResetForTests();
            BlaziumPlayerSession session = BlaziumUnitySignIn.Create(PlayerSessionTests.Game);
            session.Clock = delegate { return Now; };
            string sessionText = PlayerSessionTests.Jwt("u-1", Now, Now + 3 * 3600, "create");
            try
            {
                string rejected = session.Use(sessionText);
                Program.Check(rejected.Length == 0 && session.LoggedOn, "default Create kept the sign-in in memory");
            }
            catch (ArgumentException)
            {
                Program.Check(true, "default Create refused to start without a caller-supplied store");
            }
            AssertStoreHides(sessionText, "default Create");
        }

        private static void SignIn()
        {
            PlayerPrefs.ResetForTests();
            FakeService fake = new FakeService();
            fake.Now = Now;
            BlaziumPlayerSession session = new BlaziumPlayerSession(fake, PlayerSessionTests.Game, new BlaziumPlayerPrefsStore(PlayerSessionTests.Game));
            session.Clock = delegate { return fake.Now; };
            FinishSignIn(session, fake);
            Program.Check(session.LoggedOn && session.Export().Length > 0, "sign-in kept the session in memory");
            AssertStoreHides(session.Export(), "sign-in");
        }

        private static void Refresh()
        {
            PlayerPrefs.ResetForTests();
            FakeService fake = new FakeService();
            fake.Now = Now;
            BlaziumPlayerSession session = new BlaziumPlayerSession(fake, PlayerSessionTests.Game, new BlaziumPlayerPrefsStore(PlayerSessionTests.Game));
            session.Clock = delegate { return fake.Now; };
            FinishSignIn(session, fake);
            string first = session.Export();
            Program.Run(session.Refresh(true));
            Program.Check(session.LoggedOn && session.Export() != first && session.Export().Length > 0, "refresh kept a new in-memory session");
            AssertStoreHides(first, "refresh of the original session");
            AssertStoreHides(session.Export(), "refresh");
        }

        private static void UseExplicitStore()
        {
            PlayerPrefs.ResetForTests();
            HeaderTransport transport = new HeaderTransport();
            BlaziumPlayerSession session = new BlaziumPlayerSession(transport, PlayerSessionTests.Game, new BlaziumPlayerPrefsStore(PlayerSessionTests.Game));
            session.Clock = delegate { return Now; };
            string sessionText = PlayerSessionTests.Jwt("u-1", Now, Now + 3 * 3600, "use");
            string rejected = session.Use(sessionText);
            Program.Check(rejected.Length == 0 && session.LoggedOn, "Use kept the session in memory");
            AssertStoreHides(sessionText, "Use");
        }

        // The store method Adopt calls. A fix that only skips one caller still fails here.
        private static void DirectSave()
        {
            PlayerPrefs.ResetForTests();
            string sessionText = PlayerSessionTests.Jwt("u-1", Now, Now + 3 * 3600, "save");
            new BlaziumPlayerPrefsStore(PlayerSessionTests.Game).Save(sessionText);
            AssertStoreHides(sessionText, "PlayerPrefs store Save");
        }

        private static void FinishSignIn(BlaziumPlayerSession session, FakeService fake)
        {
            fake.LoginSession = PlayerSessionTests.Jwt("u-1", fake.Now, fake.Now + 3 * 3600, "login");
            fake.ValidSession = fake.LoginSession;
            fake.LoginPhase = "complete";
            Program.Run(session.SignIn());
        }

        private static void AssertStoreHides(string sessionText, string what)
        {
            string keys = KeysHolding(sessionText);
            Program.Check(keys.Length == 0, what + " left the session in PlayerPrefs under " + (keys.Length == 0 ? "(none)" : keys));
            string sent = EndpointsThatSend(sessionText);
            Program.Check(sent.Length == 0, what + " let a preference reader send that session as bearer to " + (sent.Length == 0 ? "(none)" : sent));
        }

        private static string KeysHolding(string sessionText)
        {
            StringBuilder keys = new StringBuilder();
            foreach (PreferenceFile.Entry entry in PreferenceFile.Snapshot())
            {
                if (HoldsSession(entry.Value, sessionText) && keys.ToString().IndexOf(entry.Key, StringComparison.Ordinal) < 0)
                {
                    if (keys.Length > 0)
                    {
                        keys.Append(",");
                    }
                    keys.Append(entry.Key);
                }
            }
            return keys.ToString();
        }

        private static bool HoldsSession(string stored, string sessionText)
        {
            if (string.IsNullOrEmpty(stored) || string.IsNullOrEmpty(sessionText))
            {
                return false;
            }
            if (stored.IndexOf(sessionText, StringComparison.Ordinal) >= 0)
            {
                return true;
            }
            string decoded = DecodeBase64(stored);
            if (decoded.IndexOf(sessionText, StringComparison.Ordinal) >= 0)
            {
                return true;
            }
            decoded = DecodeHex(stored);
            return decoded.IndexOf(sessionText, StringComparison.Ordinal) >= 0;
        }

        // Plays the stored bytes back through BlaziumPlayerSession's refresh, token, chat, account, and logout calls.
        private static string EndpointsThatSend(string sessionText)
        {
            List<string> candidates = new List<string>();
            foreach (PreferenceFile.Entry entry in PreferenceFile.Snapshot())
            {
                AddCandidate(candidates, entry.Value);
                if (entry.Value != null && sessionText.Length > 0 && entry.Value.IndexOf(sessionText, StringComparison.Ordinal) >= 0)
                {
                    AddCandidate(candidates, sessionText);
                }
                AddCandidate(candidates, DecodeBase64(entry.Value));
                AddCandidate(candidates, DecodeHex(entry.Value));
            }
            StringBuilder sent = new StringBuilder();
            foreach (string candidate in candidates)
            {
                NoteEndpoints(sent, candidate, sessionText);
            }
            return sent.ToString();
        }

        private static void AddCandidate(List<string> candidates, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }
            if (!candidates.Contains(value))
            {
                candidates.Add(value);
            }
        }

        private static void NoteEndpoints(StringBuilder sent, string candidate, string sessionText)
        {
            HeaderTransport transport = new HeaderTransport();
            transport.RefreshToken = PlayerSessionTests.Jwt("u-1", Now, Now + 3 * 3600, "renew-placeholder");
            BlaziumPlayerSession attacker = new BlaziumPlayerSession(transport, PlayerSessionTests.Game, new BlaziumMemorySessionStore());
            attacker.Clock = delegate { return Now; };
            if (attacker.Use(candidate).Length > 0)
            {
                return;
            }
            BlaziumTokenReply reply = new BlaziumTokenReply();
            Program.Run(attacker.Get("sapi", reply));
            Program.Run(attacker.Get("lobby", reply));
            Program.Run(attacker.Get("server", reply));
            Program.Run(attacker.GetChatAccess(new BlaziumChatAccess()));
            Program.Run(attacker.LoadProfile());
            // Refresh replaces the in-memory session after it sends, so it runs once the other calls have used the stored value.
            Program.Run(attacker.Refresh(true));
            CollectBearer(sent, transport, sessionText);
            HeaderTransport signingOutTransport = new HeaderTransport();
            BlaziumPlayerSession signingOut = new BlaziumPlayerSession(signingOutTransport, PlayerSessionTests.Game, new BlaziumMemorySessionStore());
            signingOut.Clock = delegate { return Now; };
            if (signingOut.Use(candidate).Length == 0)
            {
                Program.Run(signingOut.Logout());
                CollectBearer(sent, signingOutTransport, sessionText);
            }
            return;
        }

        private static void CollectBearer(StringBuilder sent, HeaderTransport transport, string sessionText)
        {
            foreach (BlaziumSapiRequest request in transport.Sent)
            {
                string auth;
                if (!request.Headers.TryGetValue("Authorization", out auth) || auth != "Bearer " + sessionText)
                {
                    continue;
                }
                string name = EndpointName(request);
                if (name.Length > 0 && sent.ToString().IndexOf(name, StringComparison.Ordinal) < 0)
                {
                    if (sent.Length > 0)
                    {
                        sent.Append(",");
                    }
                    sent.Append(name);
                }
            }
        }

        private static string EndpointName(BlaziumSapiRequest request)
        {
            string url = request.Url ?? string.Empty;
            if (url.IndexOf("/public/token/refresh", StringComparison.Ordinal) >= 0)
            {
                return "refresh";
            }
            if (url.IndexOf("/tokens", StringComparison.Ordinal) >= 0)
            {
                if (request.Body.IndexOf("server", StringComparison.Ordinal) >= 0)
                {
                    return "server";
                }
                if (request.Body.IndexOf("lobby", StringComparison.Ordinal) >= 0)
                {
                    return "lobby";
                }
                return "sapi";
            }
            if (url.IndexOf("/private/irc/token", StringComparison.Ordinal) >= 0)
            {
                return "chat";
            }
            if (url.IndexOf("/private/account", StringComparison.Ordinal) >= 0)
            {
                return "account";
            }
            if (url.IndexOf("/private/logout", StringComparison.Ordinal) >= 0)
            {
                return "logout";
            }
            return string.Empty;
        }

        private static string DecodeBase64(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length > 8192)
            {
                return string.Empty;
            }
            string padded = text.Replace('-', '+').Replace('_', '/');
            while (padded.Length % 4 != 0)
            {
                padded += "=";
            }
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            }
            catch (FormatException)
            {
                return string.Empty;
            }
        }

        private static string DecodeHex(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length < 2 || (text.Length % 2) != 0 || text.Length > 8192)
            {
                return string.Empty;
            }
            byte[] bytes = new byte[text.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                int hi = Hex(text[i * 2]);
                int lo = Hex(text[i * 2 + 1]);
                if (hi < 0 || lo < 0)
                {
                    return string.Empty;
                }
                bytes[i] = (byte)((hi << 4) | lo);
            }
            return Encoding.UTF8.GetString(bytes);
        }

        private static int Hex(char c)
        {
            if (c >= '0' && c <= '9')
            {
                return c - '0';
            }
            if (c >= 'a' && c <= 'f')
            {
                return c - 'a' + 10;
            }
            if (c >= 'A' && c <= 'F')
            {
                return c - 'A' + 10;
            }
            return -1;
        }
    }
}
