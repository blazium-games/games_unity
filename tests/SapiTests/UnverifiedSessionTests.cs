using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Blazium.Games;

namespace Blazium.Games.Tests
{
    // Answers the way an account or refresh call would. A bearer is accepted only when it is the
    // one AcceptToken names, and the accepted player is AcceptUser from that response, not the
    // user id written in the token.
    internal sealed class AccountGate : IBlaziumSapiTransport
    {
        public string AcceptToken = string.Empty;
        public string AcceptUser = string.Empty;
        public int RefusalStatus = 401;
        public bool Unreachable;
        public string AuthorizedUser = string.Empty;
        public readonly List<string> Urls = new List<string>();

        public IEnumerator Wait(float seconds)
        {
            yield break;
        }

        public IEnumerator Send(BlaziumSapiRequest request, BlaziumSapiResult result)
        {
            Urls.Add(request.Url);
            string auth;
            request.Headers.TryGetValue("Authorization", out auth);
            string bearer = auth ?? string.Empty;
            string presented = bearer.StartsWith("Bearer ", StringComparison.Ordinal) ? bearer.Substring("Bearer ".Length) : string.Empty;
            if (Unreachable)
            {
                result.Status = 0;
                result.Body = string.Empty;
                yield break;
            }
            if (AcceptToken.Length > 0 && presented == AcceptToken)
            {
                result.Status = 200;
                result.Body = "{\"user_uid\":\"" + AcceptUser + "\",\"display_name\":\"Pat\",\"username\":\"pat\",\"token\":\"" + presented + "\"}";
                AuthorizedUser = AcceptUser;
                yield break;
            }
            result.Status = RefusalStatus;
            result.Body = "{\"error\":\"refused\"}";
        }

        public bool Saw(string needle)
        {
            foreach (string url in Urls)
            {
                if (url.Contains(needle))
                {
                    return true;
                }
            }
            return false;
        }
    }

    // Remembers every session handed to the store, including one that is written and later cleared.
    internal sealed class RecordingStore : IBlaziumSessionStore
    {
        public readonly List<string> Writes = new List<string>();
        public int Clears;
        private string held = string.Empty;

        public void Plant(string session)
        {
            held = session ?? string.Empty;
        }

        public string Load()
        {
            return held;
        }

        public void Save(string session)
        {
            held = session ?? string.Empty;
            Writes.Add(held);
        }

        public void Clear()
        {
            Clears++;
            held = string.Empty;
        }
    }

    // Load returns a different session each time, so a check of the first value cannot authorize the next.
    internal sealed class SwappingStore : IBlaziumSessionStore
    {
        public readonly List<string> Values = new List<string>();
        public readonly List<string> Writes = new List<string>();
        private int loads;

        public string Load()
        {
            int at = loads;
            loads++;
            if (Values.Count == 0)
            {
                return string.Empty;
            }
            if (at >= Values.Count)
            {
                return Values[Values.Count - 1];
            }
            return Values[at];
        }

        public void Save(string session)
        {
            Writes.Add(session ?? string.Empty);
        }

        public void Clear()
        {
            Writes.Add(string.Empty);
        }
    }

    internal sealed class Probe
    {
        public bool Violated;
        public string Seen = string.Empty;
    }

    // A session string supplied by the player (Use, or a store Restore will load) must not become
    // the signed-in player unless the account or refresh call accepts that same string and names
    // the same player. An alg of none, or an empty signature segment, is never a session.
    internal static class UnverifiedSessionTests
    {
        private const long Now = 1700000000L;

        public static void Run()
        {
            long soon = Now + 3 * 3600;
            string none = Token("{\"alg\":\"none\",\"typ\":\"JWT\"}", Claims("user_uid", "claimed-user", Now, soon), "unchecked");
            string noneCaps = Token("{\"alg\":\"NONE\",\"typ\":\"JWT\"}", Claims("user_uid", "claimed-user", Now, soon), "unchecked");
            string noneMixed = Token("{\"alg\":\"None\",\"typ\":\"JWT\"}", Claims("user_uid", "claimed-user", Now, soon), "unchecked");
            string emptySig = Token("{\"alg\":\"HS256\",\"typ\":\"JWT\"}", Claims("user_uid", "claimed-user", Now, soon), string.Empty);
            string uncheckedSig = Token("{\"alg\":\"HS256\",\"typ\":\"JWT\"}", Claims("user_uid", "claimed-user", Now, soon), "unchecked");
            string uidClaim = Token("{\"alg\":\"HS256\",\"typ\":\"JWT\"}", Claims("uid", "claimed-user", Now, soon), "unchecked");
            string other = Token("{\"alg\":\"HS256\",\"typ\":\"JWT\"}", Claims("user_uid", "other-user", Now, soon), "unchecked");
            string aged = Token("{\"alg\":\"none\",\"typ\":\"JWT\"}", Claims("user_uid", "claimed-user", Now - 7200, Now + 60), "unchecked");

            RejectUse("use rejects alg none when the account call returns 401", none, 401, false);
            RejectUse("use rejects alg none when the account call returns 403", none, 403, false);
            RejectUse("use rejects alg none when the account call returns 500", none, 500, false);
            RejectUse("use rejects alg none when the account call cannot be reached", none, 0, true);
            RejectUse("use rejects an empty signature when the account call returns 401", emptySig, 401, false);
            RejectUse("use rejects an unchecked signature when the account call returns 401", uncheckedSig, 401, false);
            RejectUse("use rejects a uid claim with an unchecked signature when the account call returns 401", uidClaim, 401, false);

            RejectDespiteAcceptance("use rejects alg none even when the account call accepts that token", none, "claimed-user");
            RejectDespiteAcceptance("use rejects alg NONE even when the account call accepts that token", noneCaps, "claimed-user");
            RejectDespiteAcceptance("use rejects alg None even when the account call accepts that token", noneMixed, "claimed-user");
            RejectDespiteAcceptance("use rejects an empty signature even when the account call accepts that token", emptySig, "claimed-user");

            MismatchUse("use does not sign in as the user id carried in a token the account call assigns to someone else", uncheckedSig, "authorized-user");
            AcceptOnlyAfterTheCall("use records the player only after the account call names that same player", uncheckedSig, "claimed-user");
            UseDoesNotSwap("use authorizes the session it was given, not a different session loaded afterwards", uncheckedSig, other);

            RejectRestore("restore rejects alg none when the account call returns 401", none, 401, false);
            RejectRestore("restore rejects alg none when the account call returns 403", none, 403, false);
            RejectRestore("restore rejects alg none when the account call returns 500", none, 500, false);
            RejectRestore("restore rejects alg none when the account call cannot be reached", none, 0, true);
            RejectRestore("restore rejects alg none when renewal is due and the service cannot be reached", aged, 0, true);
            RejectRestore("restore rejects an empty signature when the account call returns 500", emptySig, 500, false);
            RejectRestore("restore rejects an unchecked signature when the account call returns 403", uncheckedSig, 403, false);
            RejectDespiteAcceptanceRestore("restore rejects alg none even when the account call accepts that token", none);
            MismatchRestore("restore does not keep a claimed user the account call does not name", uncheckedSig, "authorized-user");
            RestoreDoesNotSwap("restore authorizes the session it checked, not a later session from the store", uncheckedSig, other);

            ForgedSessionCannotTakeTokens(uncheckedSig);
        }

        private static void RejectUse(string name, string forged, int status, bool unreachable)
        {
            AccountGate gate = Refuse(status, unreachable);
            RecordingStore store = Marker();
            Probe probe = new Probe();
            BlaziumPlayerSession session = Start(gate, store, probe);
            string error = session.Use(forged);
            Program.Check(error.Length > 0 && SignedOut(session, probe, store, forged, true) && !gate.Saw("/tokens") && !gate.Saw("/private/irc"), name);
        }

        private static void RejectDespiteAcceptance(string name, string forged, string named)
        {
            AccountGate gate = Accept(forged, named);
            RecordingStore store = Marker();
            Probe probe = new Probe();
            BlaziumPlayerSession session = Start(gate, store, probe);
            string error = session.Use(forged);
            Program.Check(error.Length > 0 && SignedOut(session, probe, store, forged, true), name);
        }

        private static void MismatchUse(string name, string forged, string named)
        {
            AccountGate gate = Accept(forged, named);
            RecordingStore store = Marker();
            Probe probe = new Probe();
            BlaziumPlayerSession session = Start(gate, store, probe);
            session.Use(forged);
            bool identity = session.UserUid != "claimed-user" && (!session.LoggedOn || session.UserUid == named);
            bool kept = !Wrote(store, forged) || session.UserUid == named;
            Program.Check(!probe.Violated && identity && kept && store.Load() != forged, name);
        }

        private static void AcceptOnlyAfterTheCall(string name, string forged, string named)
        {
            AccountGate gate = Accept(forged, named);
            RecordingStore store = Marker();
            Probe probe = new Probe();
            BlaziumPlayerSession session = Start(gate, store, probe);
            session.Use(forged);
            bool end = !session.LoggedOn ? session.UserUid.Length == 0 && session.Export().Length == 0 : session.UserUid == named && session.State == BlaziumSignInState.Complete;
            Program.Check(!probe.Violated && end && session.UserUid != "other-user", name);
        }

        private static void UseDoesNotSwap(string name, string given, string loadedLater)
        {
            AccountGate gate = Accept(given, "claimed-user");
            SwappingStore store = new SwappingStore();
            store.Values.Add(loadedLater);
            Probe probe = new Probe();
            BlaziumPlayerSession session = Start(gate, store, probe);
            session.Use(given);
            bool wroteOther = false;
            foreach (string saved in store.Writes)
            {
                if (saved == loadedLater)
                {
                    wroteOther = true;
                }
            }
            bool end = !session.LoggedOn ? session.UserUid.Length == 0 : session.UserUid == "claimed-user";
            Program.Check(!probe.Violated && end && session.UserUid != "other-user" && !wroteOther, name);
        }

        private static void RejectRestore(string name, string forged, int status, bool unreachable)
        {
            AccountGate gate = Refuse(status, unreachable);
            RecordingStore store = new RecordingStore();
            store.Plant(forged);
            Probe probe = new Probe();
            BlaziumPlayerSession session = Start(gate, store, probe);
            Program.Run(session.Restore());
            Program.Check(SignedOut(session, probe, store, forged, false) && !gate.Saw("/tokens") && !gate.Saw("/private/irc"), name);
        }

        private static void RejectDespiteAcceptanceRestore(string name, string forged)
        {
            AccountGate gate = Accept(forged, "claimed-user");
            RecordingStore store = new RecordingStore();
            store.Plant(forged);
            Probe probe = new Probe();
            BlaziumPlayerSession session = Start(gate, store, probe);
            Program.Run(session.Restore());
            Program.Check(SignedOut(session, probe, store, forged, false), name);
        }

        private static void MismatchRestore(string name, string forged, string named)
        {
            AccountGate gate = Accept(forged, named);
            RecordingStore store = new RecordingStore();
            store.Plant(forged);
            Probe probe = new Probe();
            BlaziumPlayerSession session = Start(gate, store, probe);
            Program.Run(session.Restore());
            bool identity = session.UserUid != "claimed-user" && (!session.LoggedOn || session.UserUid == named);
            Program.Check(!probe.Violated && identity && session.Export() != forged, name);
        }

        private static void RestoreDoesNotSwap(string name, string first, string second)
        {
            AccountGate gate = Accept(first, "claimed-user");
            SwappingStore store = new SwappingStore();
            store.Values.Add(first);
            store.Values.Add(second);
            Probe probe = new Probe();
            BlaziumPlayerSession session = Start(gate, store, probe);
            Program.Run(session.Restore());
            bool wroteOther = false;
            foreach (string saved in store.Writes)
            {
                if (saved == second)
                {
                    wroteOther = true;
                }
            }
            bool end = !session.LoggedOn ? session.UserUid.Length == 0 : session.UserUid == "claimed-user";
            Program.Check(!probe.Violated && end && session.UserUid != "other-user" && !wroteOther && session.Export() != second, name);
        }

        private static void ForgedSessionCannotTakeTokens(string forged)
        {
            AccountGate gate = Refuse(401, false);
            RecordingStore store = Marker();
            Probe probe = new Probe();
            BlaziumPlayerSession session = Start(gate, store, probe);
            session.Use(forged);
            BlaziumTokenReply sapi = Ask(session, "sapi");
            BlaziumTokenReply lobby = Ask(session, "lobby");
            BlaziumTokenReply server = Ask(session, "server");
            BlaziumChatAccess chat = new BlaziumChatAccess();
            Program.Run(session.GetChatAccess(chat));
            bool noToken = sapi.Code == "not_logged_on" && lobby.Code == "not_logged_on" && server.Code == "not_logged_on";
            bool noSecret = sapi.Token.Length == 0 && lobby.Token.Length == 0 && server.Token.Length == 0 && chat.Token.Length == 0 && !chat.Ok;
            Program.Check(SignedOut(session, probe, store, forged, true) && noToken && noSecret && !gate.Saw("/tokens") && !gate.Saw("/private/irc"), "a forged session cannot obtain a sapi, lobby, or server token, or chat access");
        }

        private static AccountGate Refuse(int status, bool unreachable)
        {
            AccountGate gate = new AccountGate();
            gate.RefusalStatus = status == 0 ? 401 : status;
            gate.Unreachable = unreachable;
            return gate;
        }

        private static AccountGate Accept(string token, string user)
        {
            AccountGate gate = new AccountGate();
            gate.AcceptToken = token;
            gate.AcceptUser = user;
            return gate;
        }

        private static RecordingStore Marker()
        {
            RecordingStore store = new RecordingStore();
            store.Plant("previous-session-marker");
            return store;
        }

        private static BlaziumPlayerSession Start(IBlaziumSapiTransport gate, IBlaziumSessionStore store, Probe probe)
        {
            AccountGate account = gate as AccountGate;
            BlaziumPlayerSession session = new BlaziumPlayerSession(gate, PlayerSessionTests.Game, store);
            session.Clock = delegate { return Now; };
            session.Changed += delegate
            {
                string authorized = account != null ? account.AuthorizedUser : string.Empty;
                if (session.UserUid.Length > 0 && session.UserUid != authorized)
                {
                    probe.Violated = true;
                    probe.Seen = session.UserUid;
                }
                if (session.LoggedOn && (authorized.Length == 0 || session.UserUid != authorized))
                {
                    probe.Violated = true;
                    probe.Seen = session.UserUid;
                }
            };
            return session;
        }

        private static bool SignedOut(BlaziumPlayerSession session, Probe probe, RecordingStore store, string forged, bool keepMarker)
        {
            bool marker = !keepMarker || (store.Clears == 0 && store.Load() == "previous-session-marker");
            return !probe.Violated && !session.LoggedOn && session.UserUid.Length == 0 && session.State != BlaziumSignInState.Complete && session.Export().Length == 0 && !Wrote(store, forged) && marker;
        }

        private static bool Wrote(RecordingStore store, string forged)
        {
            foreach (string saved in store.Writes)
            {
                if (saved == forged)
                {
                    return true;
                }
            }
            return false;
        }

        private static BlaziumTokenReply Ask(BlaziumPlayerSession session, string audience)
        {
            BlaziumTokenReply reply = new BlaziumTokenReply();
            Program.Run(session.Get(audience, reply));
            return reply;
        }

        private static string Claims(string field, string user, long iat, long exp)
        {
            return "{\"" + field + "\":\"" + user + "\",\"iat\":" + iat + ",\"exp\":" + exp + "}";
        }

        private static string Token(string header, string payload, string signature)
        {
            return Segment(header) + "." + Segment(payload) + "." + signature;
        }

        private static string Segment(string json)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}
