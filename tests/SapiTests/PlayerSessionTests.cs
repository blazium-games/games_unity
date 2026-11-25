using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Blazium.Games;

namespace Blazium.Games.Tests
{
    // Plays games_login and games_service for the launcher-less player session.
    internal sealed class FakeService : IBlaziumSapiTransport
    {
        public readonly List<string> Log = new List<string>();
        public string ValidSession = string.Empty;
        public string LoginPhase = "waiting";
        public string LoginSession = string.Empty;
        public int TokensIssued;
        public int Refreshes;
        public long Now;
        public bool LoginRefused;
        public int VerifyStatus = 200;
        public bool Unreachable;

        public int Count(string needle)
        {
            int n = 0;
            foreach (string line in Log)
            {
                if (line.Contains(needle))
                {
                    n++;
                }
            }
            return n;
        }

        public IEnumerator Wait(float seconds)
        {
            yield break;
        }

        public IEnumerator Send(BlaziumSapiRequest request, BlaziumSapiResult result)
        {
            Log.Add(request.Method + " " + request.Url);
            result.Status = 0;
            result.Body = string.Empty;
            if (Unreachable)
            {
                yield break;
            }
            string auth;
            request.Headers.TryGetValue("Authorization", out auth);
            auth = auth ?? string.Empty;
            bool authed = ValidSession.Length > 0 && auth == "Bearer " + ValidSession;
            string url = request.Url;
            result.Status = 200;
            if (url.Contains("login.blazium.online/api/v1/session"))
            {
                if (request.Method == "POST")
                {
                    if (LoginRefused)
                    {
                        result.Status = 409;
                        result.Body = "{\"ok\":false,\"error\":\"No configured auth providers\"}";
                        yield break;
                    }
                    result.Body = "{\"ok\":true,\"cid\":\"cid-1\",\"secret\":\"secret-1\",\"login_url\":\"https://blazium.games/connect/cid-1\",\"user_code\":\"ABCD-EFGH\",\"verification_url\":\"https://blazium.games/connect\",\"expires_in\":1800}";
                    yield break;
                }
                if (request.Method == "DELETE")
                {
                    result.Body = "{\"ok\":true}";
                    yield break;
                }
                if (auth != "Bearer secret-1")
                {
                    result.Status = 404;
                    result.Body = "{\"ok\":false,\"state\":\"gone\"}";
                    yield break;
                }
                if (LoginPhase == "complete")
                {
                    result.Body = "{\"ok\":true,\"state\":\"complete\",\"jwt\":\"" + LoginSession + "\",\"user_id\":\"u-1\",\"display_name\":\"Ada\"}";
                }
                else if (LoginPhase == "pending")
                {
                    result.Body = "{\"ok\":true,\"state\":\"pending\",\"gate\":\"legal\"}";
                }
                else
                {
                    result.Body = "{\"ok\":true,\"state\":\"waiting\"}";
                }
                yield break;
            }
            if (url.Contains("/server/verify-player"))
            {
                string key;
                request.Headers.TryGetValue("X-Server-Key", out key);
                if (key != "bgs_key")
                {
                    result.Status = 401;
                    result.Body = "{\"error\":{\"code\":4010,\"message\":\"Server key required\"}}";
                }
                else if (VerifyStatus == 409)
                {
                    result.Status = 409;
                    result.Body = "{\"error\":{\"code\":4098,\"message\":\"This player token was already used\"}}";
                }
                else
                {
                    result.Body = "{\"data\":{\"user_uid\":\"u-1\",\"game_uid\":\"" + PlayerSessionTests.Game + "\"}}";
                }
                yield break;
            }
            if (url.Contains("/public/token/refresh"))
            {
                if (!authed)
                {
                    result.Status = 401;
                    result.Body = "{\"error\":\"expired\"}";
                    yield break;
                }
                Refreshes++;
                ValidSession = PlayerSessionTests.Jwt("u-1", Now, Now + 3 * 3600, "r" + Refreshes);
                result.Body = "{\"token\":\"" + ValidSession + "\"}";
                yield break;
            }
            if (!authed)
            {
                result.Status = 401;
                result.Body = "{\"error\":{\"code\":4010,\"message\":\"Missing token\"}}";
                yield break;
            }
            if (url.Contains("/private/account"))
            {
                result.Body = "{\"username\":\"ada\",\"display_name\":\"Ada L\",\"avatar\":\"x\"}";
            }
            else if (url.Contains("/tokens"))
            {
                TokensIssued++;
                string aud = request.Body.Contains("server") ? "server" : (request.Body.Contains("lobby") ? "lobby" : "sapi");
                result.Body = "{\"data\":{\"token\":\"scoped-" + aud + "-" + TokensIssued + "\",\"expires_in\":900,\"expires_at\":\"2099-01-01T00:00:00Z\"}}";
            }
            else if (url.Contains("/private/irc/token"))
            {
                result.Body = "{\"username\":\"ada\",\"token\":\"irc-secret\",\"host\":\"irc.blazium.online\",\"port\":6697,\"tls\":true,\"websocket\":\"wss://irc.blazium.online/ws\",\"services_nick\":\"Services\"}";
            }
            else if (url.Contains("/private/logout"))
            {
                result.Body = "{\"ok\":true}";
            }
            else
            {
                result.Status = 404;
            }
        }
    }

    // Steps a coroutine one yield at a time, so a test can change the server between polls.
    internal sealed class Pump
    {
        private readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();

        public Pump(IEnumerator routine)
        {
            stack.Push(routine);
        }

        public bool Done { get { return stack.Count == 0; } }

        public void Step()
        {
            if (stack.Count == 0)
            {
                return;
            }
            IEnumerator top = stack.Peek();
            if (!top.MoveNext())
            {
                stack.Pop();
                return;
            }
            IEnumerator inner = top.Current as IEnumerator;
            if (inner != null)
            {
                stack.Push(inner);
            }
        }

        public bool StepUntil(Func<bool> predicate, int limit = 2000)
        {
            for (int i = 0; i < limit && !predicate(); i++)
            {
                Step();
                if (Done)
                {
                    break;
                }
            }
            return predicate();
        }
    }

    internal static class PlayerSessionTests
    {
        public const string Game = "6ba7b810-9dad-11d1-80b4-00c04fd430c8";

        public static string Jwt(string user, long iat, long exp, string tag)
        {
            Func<string, string> b64 = delegate (string s)
            {
                return Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            };
            return b64("{\"alg\":\"none\"}") + "." + b64("{\"user_uid\":\"" + user + "\",\"iat\":" + iat + ",\"exp\":" + exp + ",\"tag\":\"" + tag + "\"}") + "." + b64("sig");
        }

        private static void Check(bool ok, string what)
        {
            Program.Check(ok, what);
        }

        private static BlaziumTokenReply Ask(BlaziumPlayerSession session, string audience)
        {
            BlaziumTokenReply reply = new BlaziumTokenReply();
            Program.Run(session.Get(audience, reply));
            return reply;
        }

        private static void SignIn(BlaziumPlayerSession session, FakeService fake)
        {
            fake.LoginSession = Jwt("u-1", fake.Now, fake.Now + 3 * 3600, "login");
            fake.ValidSession = fake.LoginSession;
            fake.LoginPhase = "waiting";
            Pump pump = new Pump(session.SignIn());
            pump.StepUntil(delegate { return session.State == BlaziumSignInState.Waiting; });
            fake.LoginPhase = "complete";
            pump.StepUntil(delegate { return pump.Done; });
        }

        public static void Run()
        {
            long now = 1700000000;
            FakeService fake = new FakeService();
            fake.Now = now;
            BlaziumMemorySessionStore store = new BlaziumMemorySessionStore();
            BlaziumPlayerSession session = new BlaziumPlayerSession(fake, Game, store);
            session.Clock = delegate { return fake.Now; };
            int changes = 0;
            session.Changed += delegate { changes++; };

            Check(!session.LoggedOn && session.State == BlaziumSignInState.Idle, "starts signed out");
            Check(Ask(session, "sapi").Code == "not_logged_on" && fake.Count("/tokens") == 0, "no token before sign-in, and no request");
            Check(Ask(session, "admin").Code == "bad_audience", "bad audience");

            // A refused start says why.
            fake.LoginRefused = true;
            Program.Run(session.SignIn());
            Check(session.State == BlaziumSignInState.Failed && session.Error.Contains("No configured auth providers"), "failed start names why");
            fake.LoginRefused = false;

            // Link and code, then the account's step, then done.
            fake.LoginSession = Jwt("u-1", now, now + 3 * 3600, "login");
            fake.ValidSession = fake.LoginSession;
            Pump pump = new Pump(session.SignIn());
            Check(pump.StepUntil(delegate { return session.State == BlaziumSignInState.Waiting; }), "reaches waiting");
            Check(session.UserCode == "ABCD-EFGH" && session.LoginUrl == "https://blazium.games/connect/cid-1" && session.VerificationUrl == "https://blazium.games/connect", "shows link and code");
            Check(!session.LoggedOn, "not signed in while waiting");
            int posts = fake.Count("POST https://login.blazium.online/api/v1/session");
            Program.Run(session.SignIn());
            Check(fake.Count("POST https://login.blazium.online/api/v1/session") == posts, "a second start joins the first");
            fake.LoginPhase = "pending";
            Check(pump.StepUntil(delegate { return session.State == BlaziumSignInState.Pending; }) && session.Gate == "legal", "reaches pending with the gate");
            Check(!session.LoggedOn, "not signed in while pending");
            fake.LoginPhase = "complete";
            Check(pump.StepUntil(delegate { return pump.Done; }), "sign-in ends");
            Check(session.State == BlaziumSignInState.Complete && session.LoggedOn && session.UserUid == "u-1", "signed in");
            Check(session.DisplayName == "Ada L" && session.Username == "ada", "profile loaded");
            Check(store.Load() == fake.LoginSession, "session saved");
            Check(changes > 4, "changes were announced");
            Check(!session.Export().Contains(" ") && session.Export() == fake.LoginSession, "export");

            // Tokens: asked once, cached, one per audience, never the session.
            BlaziumTokenReply sapi = Ask(session, "sapi");
            Check(sapi.Ok && sapi.Token == "scoped-sapi-1" && sapi.GameUid == Game && sapi.Audience == "sapi", "sapi token");
            Check(Ask(session, "sapi").Token == "scoped-sapi-1" && fake.TokensIssued == 1, "token cached");
            Check(Ask(session, "server").Token == "scoped-server-2", "server token");
            Check(sapi.Token != session.Export(), "the session is never handed out as a token");

            // The Scripted API client works with the session as its token source.
            FakeTransport sapiTransport = new FakeTransport();
            BlaziumSapi client = new BlaziumSapi(sapiTransport, session);
            client.GameUid = Game;
            BlaziumSapiResult r = new BlaziumSapiResult();
            Program.Run(client.Call("score_submit", "POST", "{\"score\":1}", r));
            Check(r.Success && sapiTransport.Sent[0].Headers["Authorization"] == "Bearer scoped-sapi-1", "sapi call uses the cached token");

            // Renewal: due in the last third; tokens made with the old session are dropped.
            Program.Run(session.Refresh(false));
            Check(fake.Refreshes == 0, "no renewal early");
            fake.Now += 2 * 3600 + 60;
            BlaziumTokenReply afterRenew = Ask(session, "sapi");
            Check(fake.Refreshes == 1 && afterRenew.Token == "scoped-sapi-3", "renews when due, then asks for a fresh token");
            Check(store.Load() == fake.ValidSession, "renewed session saved");

            // Chat: the ten minute password, never the session.
            BlaziumChatAccess chat = new BlaziumChatAccess();
            Program.Run(session.GetChatAccess(chat));
            Check(chat.Ok && chat.Host == "irc.blazium.online" && chat.Port == 6697 && chat.Tls && chat.Username == "ada", "chat access");
            Check(chat.Token == "irc-secret" && chat.Token != session.Export() && !chat.ToString().Contains("irc-secret"), "chat password is not the session and is not printed");

            // Logout ends it on the service and forgets the copy.
            Program.Run(session.Logout());
            Check(!session.LoggedOn && session.State == BlaziumSignInState.Idle && store.Load().Length == 0, "logged out");
            Check(fake.Count("POST https://api.blazium.online/api/v1/private/logout") == 1, "service told");
            Check(Ask(session, "sapi").Code == "not_logged_on", "no token after logout");
            Program.Run(session.GetChatAccess(chat));
            Check(!chat.Ok && chat.Code == "not_logged_on" && chat.Token.Length == 0, "no chat after logout");

            // Restore from the store, and an ended saved session is dropped.
            fake.ValidSession = Jwt("u-1", fake.Now, fake.Now + 3600, "saved");
            store.Save(fake.ValidSession);
            Program.Run(session.Restore());
            Check(session.LoggedOn && session.UserUid == "u-1", "restored");
            store.Clear();
            BlaziumPlayerSession late = new BlaziumPlayerSession(fake, Game, store);
            late.Clock = delegate { return fake.Now; };
            store.Save(Jwt("u-1", fake.Now - 7200, fake.Now - 10, "old"));
            Program.Run(late.Restore());
            Check(!late.LoggedOn && store.Load().Length == 0, "ended saved session dropped");
            Check(session.Use("not.a.jwt").Length > 0, "unusable session refused");

            // The service ends the session: the game finds out and is signed out.
            fake.ValidSession = "something-else";
            BlaziumTokenReply refused = Ask(session, "lobby");
            Check(refused.Code == "not_logged_on" && refused.Token.Length == 0 && !session.LoggedOn, "ended session signs out");

            // Cancel gives the sign-in back.
            fake.Log.Clear();
            BlaziumPlayerSession c = new BlaziumPlayerSession(fake, Game, null);
            c.Clock = delegate { return fake.Now; };
            Pump cp = new Pump(c.SignIn());
            cp.StepUntil(delegate { return c.State == BlaziumSignInState.Waiting; });
            c.Cancel();
            Check(c.State == BlaziumSignInState.Cancelled && c.LoginUrl.Length == 0, "cancelled");
            cp.StepUntil(delegate { return cp.Done; });
            Check(fake.Count("DELETE https://login.blazium.online/api/v1/session/cid-1") == 1, "session given back");

            // A service that cannot be reached fails the sign-in after a few tries, not forever.
            FakeService down = new FakeService();
            down.Now = now;
            down.Unreachable = true;
            BlaziumPlayerSession d = new BlaziumPlayerSession(down, Game, null);
            d.Clock = delegate { return down.Now; };
            Program.Run(d.SignIn());
            Check(d.State == BlaziumSignInState.Failed && d.Error.Contains("could not be reached"), "unreachable fails the sign-in");

            // A sign-in that outlives the login's lifetime ends.
            FakeService slow = new FakeService();
            slow.Now = now;
            BlaziumPlayerSession s = new BlaziumPlayerSession(slow, Game, null);
            s.Clock = delegate { return slow.Now; };
            Pump sp = new Pump(s.SignIn());
            sp.StepUntil(delegate { return s.State == BlaziumSignInState.Waiting; });
            slow.Now += 32 * 60;
            sp.StepUntil(delegate { return sp.Done; });
            Check(s.State == BlaziumSignInState.Failed && s.Error.Contains("too long"), "an old sign-in times out");

            // Claims.
            string user;
            long exp;
            long iat;
            Check(BlaziumPlayerSession.ReadClaims(Jwt("u-9", 5, 10, "x"), out user, out exp, out iat) && user == "u-9" && exp == 10 && iat == 5, "claims read");
            Check(!BlaziumPlayerSession.ReadClaims("a.b", out user, out exp, out iat), "two parts are not a session");
            Check(!BlaziumPlayerSession.ReadClaims("a.!!!.c", out user, out exp, out iat), "bad base64 is not a session");
            Check(!BlaziumPlayerSession.ReadClaims("", out user, out exp, out iat), "empty is not a session");

            // A server proves who a player is.
            FakeService srv = new FakeService();
            BlaziumPlayerProof proof = new BlaziumPlayerProof();
            Program.Run(BlaziumPlayerVerifier.Verify(srv, "bgs_key", "player-token", proof));
            Check(proof.Ok && proof.UserUid == "u-1" && proof.GameUid == Game, "player proven");
            Program.Run(BlaziumPlayerVerifier.Verify(srv, "bgs_wrong", "t", proof));
            Check(!proof.Ok && proof.Code == "refused", "wrong key refused");
            srv.VerifyStatus = 409;
            Program.Run(BlaziumPlayerVerifier.Verify(srv, "bgs_key", "t", proof));
            Check(!proof.Ok && proof.Code == "already_used", "a token works once");
            Program.Run(BlaziumPlayerVerifier.Verify(srv, "", "t", proof));
            Check(proof.Code == "bad_request", "key required");
            srv.Unreachable = true;
            Program.Run(BlaziumPlayerVerifier.Verify(srv, "bgs_key", "t", proof));
            Check(proof.Code == "service_unavailable", "unreachable");
        }
    }
}
