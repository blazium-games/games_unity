using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Blazium.Games
{
    public enum BlaziumSignInState
    {
        Idle,
        Starting,
        // Show LoginUrl as a link or QR code, or UserCode and VerificationUrl on a console or TV.
        Waiting,
        // The player signed in, but the account has a step left on blazium.games (see Gate).
        Pending,
        Complete,
        Failed,
        Cancelled,
    }

    // Where a signed-in session is kept between runs. The session is as good as a password: use the
    // platform's secure store (Keychain, Keystore, a console's save data) when you can.
    public interface IBlaziumSessionStore
    {
        string Load();
        void Save(string session);
        void Clear();
    }

    // Seals the saved session with the platform's secret store (DPAPI, the Keystore, the Keychain) so
    // only this player on this device can open it. Return null when the value could not be sealed or
    // opened; nothing is written then.
    public interface IBlaziumSecretProtector
    {
        byte[] Protect(byte[] plain);
        byte[] Unprotect(byte[] sealedValue);
    }

    public sealed class BlaziumMemorySessionStore : IBlaziumSessionStore
    {
        private string held = string.Empty;
        public string Load() { return held; }
        public void Save(string session) { held = session ?? string.Empty; }
        public void Clear() { held = string.Empty; }
    }

    // A player who signs in without the Blazium launcher, for a Unity game on a phone, a console, the
    // web, or a desktop with no launcher installed. It needs no native library: the sign-in, the renewal
    // and the tokens are plain HTTPS through the same transport the Scripted API client uses.
    //
    //   var session = new BlaziumPlayerSession(transport, gameUid, store);
    //   yield return session.Restore();                    // a saved sign-in, if there is one
    //   if (!session.LoggedOn) {
    //       StartCoroutine(session.SignIn());              // then read State / LoginUrl / UserCode each frame
    //   }
    //   var sapi = new BlaziumSapi(transport, session) { GameUid = gameUid };
    //
    // The verifier of the sign-in stays on the login service. The game holds a poll secret that only
    // lets it ask whether the player has signed in, and a session token the service already checked.
    public sealed class BlaziumPlayerSession : IBlaziumTokenSource
    {
        public const string DefaultApiBase = "https://api.blazium.online/api/v1";
        public const string DefaultLoginBase = "https://login.blazium.online";

        public readonly string GameUid;
        public string ApiBase = DefaultApiBase;
        public string LoginBase = DefaultLoginBase;
        // Seconds the login service may hold one poll. Short keeps a closing game quick.
        public int PollSeconds = 10;
        // Seconds since the epoch. A test replaces it.
        public Func<long> Clock = delegate { return DateTimeOffset.UtcNow.ToUnixTimeSeconds(); };
        // Raised whenever State, LoggedOn or the sign-in details change.
        public event Action Changed;

        public BlaziumSignInState State { get; private set; }
        public string LoginUrl { get; private set; }
        public string UserCode { get; private set; }
        public string VerificationUrl { get; private set; }
        public string Gate { get; private set; }
        public string Error { get; private set; }
        public int ExpiresIn { get; private set; }
        public string UserUid { get; private set; }
        public string DisplayName { get; private set; }
        public string Username { get; private set; }
        public bool LoggedOn { get { return session.Length > 0; } }

        private readonly IBlaziumSapiTransport transport;
        private readonly IBlaziumSessionStore store;
        private string session = string.Empty;
        private long expires;
        private long issued;
        private int generation;
        private int epoch;
        private long nextRefreshTry;
        private bool refreshing;
        private readonly Dictionary<string, TokenEntry> cache = new Dictionary<string, TokenEntry>();

        private sealed class TokenEntry
        {
            public BlaziumTokenReply Reply;
            public long Until;
            public int Epoch;
        }

        public BlaziumPlayerSession(IBlaziumSapiTransport transport, string gameUid, IBlaziumSessionStore store = null)
        {
            if (transport == null)
            {
                throw new ArgumentNullException("transport");
            }
            if (string.IsNullOrEmpty(gameUid))
            {
                throw new ArgumentException("the game uid is the project UUID", "gameUid");
            }
            this.transport = transport;
            this.store = store;
            GameUid = gameUid;
            Reset();
        }

        // ---- saved sign-in ---------------------------------------------------------------------------

        // Uses the session a store kept from an earlier run, once the Blazium service accepts it. An
        // ended or refused one is dropped. With no network the player stays signed out and Error says why.
        public IEnumerator Restore()
        {
            string saved = store != null ? store.Load() : string.Empty;
            if (string.IsNullOrEmpty(saved))
            {
                yield break;
            }
            yield return Verify(saved, false);
        }

        // Uses a session the game kept elsewhere, once the Blazium service accepts it, and saves it to
        // the store. It ends with State Complete, or with Error set and the player not signed in.
        public IEnumerator Restore(string savedSession)
        {
            yield return Verify(savedSession ?? string.Empty, true);
        }

        // A session is only signed in after the Blazium service accepts it, which Use cannot wait for.
        // It never signs the player in and always returns why.
        [Obsolete("Use cannot check a session with the Blazium service. yield return Restore(savedSession) instead.")]
        public string Use(string savedSession)
        {
            string user;
            long exp;
            long iat;
            if (!ReadClaims(savedSession, out user, out exp, out iat) || exp <= Clock())
            {
                return Unusable;
            }
            return "the session has to be checked with the Blazium service first: yield return Restore(savedSession)";
        }

        // The session token, for a platform secure store. Treat it as a password.
        public string Export()
        {
            return session;
        }

        // ---- sign-in -----------------------------------------------------------------------------------

        // Runs a sign-in to the end. Start it as a coroutine and watch State, or Changed. It ends with
        // Complete, Failed or Cancelled. Asking while one is running does nothing.
        public IEnumerator SignIn()
        {
            if (LoggedOn)
            {
                SetState(BlaziumSignInState.Complete);
                yield break;
            }
            if (State == BlaziumSignInState.Starting || State == BlaziumSignInState.Waiting || State == BlaziumSignInState.Pending)
            {
                yield break;
            }
            generation++;
            int mine = generation;
            LoginUrl = UserCode = VerificationUrl = Gate = Error = string.Empty;
            ExpiresIn = 0;
            SetState(BlaziumSignInState.Starting);

            BlaziumSapiRequest create = Json("POST", LoginBase.TrimEnd('/') + "/api/v1/session", "{\"game_uid\":\"" + Esc(GameUid) + "\"}", string.Empty);
            BlaziumSapiResult made = new BlaziumSapiResult();
            yield return transport.Send(create, made);
            if (mine != generation)
            {
                yield break;
            }
            if (made.Status == 0)
            {
                Fail(mine, "the sign-in service could not be reached");
                yield break;
            }
            if (made.Status != 200)
            {
                Fail(mine, Refusal(made.Body));
                yield break;
            }
            string cid = BlaziumJson.Field(made.Body, "cid");
            string secret = BlaziumJson.Field(made.Body, "secret");
            if (cid.Length == 0 || secret.Length == 0)
            {
                Fail(mine, "the sign-in service gave an unexpected answer");
                yield break;
            }
            LoginUrl = BlaziumJson.Field(made.Body, "login_url");
            UserCode = BlaziumJson.Field(made.Body, "user_code");
            VerificationUrl = BlaziumJson.Field(made.Body, "verification_url");
            int expiresIn;
            int.TryParse(BlaziumJson.Field(made.Body, "expires_in"), out expiresIn);
            ExpiresIn = expiresIn;
            SetState(BlaziumSignInState.Waiting);

            string pollUrl = LoginBase.TrimEnd('/') + "/api/v1/session/" + Uri.EscapeDataString(cid);
            string seen = "waiting";
            int failures = 0;
            long started = Clock();
            while (mine == generation)
            {
                if (Clock() - started > 31 * 60)
                {
                    Fail(mine, "the sign-in took too long");
                    yield break;
                }
                BlaziumSapiRequest poll = Json("GET", pollUrl + "?wait=" + Math.Max(1, PollSeconds) + "&seen=" + seen, string.Empty, secret);
                poll.TimeoutSeconds = Math.Max(1, PollSeconds) + 10;
                BlaziumSapiResult got = new BlaziumSapiResult();
                yield return transport.Send(poll, got);
                if (mine != generation)
                {
                    break;
                }
                if (got.Status == 0)
                {
                    failures++;
                    if (failures >= 8)
                    {
                        Fail(mine, "the sign-in service could not be reached");
                        yield break;
                    }
                    yield return transport.Wait(failures);
                    continue;
                }
                failures = 0;
                if (got.Status == 404)
                {
                    Fail(mine, "the sign-in expired; start again");
                    yield break;
                }
                if (got.Status != 200)
                {
                    Fail(mine, Refusal(got.Body));
                    yield break;
                }
                string state = BlaziumJson.Field(got.Body, "state");
                if (state == "complete")
                {
                    string jwt = BlaziumJson.Field(got.Body, "jwt");
                    if (!Adopt(jwt, BlaziumJson.Field(got.Body, "display_name"), true))
                    {
                        Fail(mine, "the sign-in service gave a session that could not be used");
                        yield break;
                    }
                    Gate = string.Empty;
                    Error = string.Empty;
                    SetState(BlaziumSignInState.Complete);
                    yield return LoadProfile();
                    yield break;
                }
                if (state == "pending")
                {
                    Gate = BlaziumJson.Field(got.Body, "gate");
                    seen = "pending";
                    SetState(BlaziumSignInState.Pending);
                }
                yield return transport.Wait(0.25f);
            }
            // Cancelled: give the sign-in back to the service.
            BlaziumSapiResult ignored = new BlaziumSapiResult();
            yield return transport.Send(Json("DELETE", pollUrl, string.Empty, secret), ignored);
        }

        // Ends a sign-in in progress. SignIn stops at its next step.
        public void Cancel()
        {
            if (State == BlaziumSignInState.Starting || State == BlaziumSignInState.Waiting || State == BlaziumSignInState.Pending)
            {
                generation++;
                LoginUrl = UserCode = VerificationUrl = Gate = string.Empty;
                SetState(BlaziumSignInState.Cancelled);
            }
        }

        // Ends the session on the service and forgets the saved copy.
        public IEnumerator Logout()
        {
            string old = session;
            generation++; // ends any sign-in in progress
            Drop(BlaziumSignInState.Idle);
            if (old.Length == 0)
            {
                yield break;
            }
            BlaziumSapiResult ignored = new BlaziumSapiResult();
            yield return transport.Send(Json("POST", ApiBase.TrimEnd('/') + "/private/logout", string.Empty, old), ignored);
        }

        // ---- keeping the session fresh -----------------------------------------------------------

        // Renews the session when it is in the last third of its life (or always, with force). Safe to
        // call often. A refusal that means the login is over signs the player out.
        public IEnumerator Refresh(bool force)
        {
            if (session.Length == 0 || refreshing)
            {
                yield break;
            }
            long now = Clock();
            if (!force && !RefreshDue(now))
            {
                yield break;
            }
            refreshing = true;
            string used = session;
            BlaziumSapiResult res = new BlaziumSapiResult();
            yield return transport.Send(Json("POST", ApiBase.TrimEnd('/') + "/public/token/refresh", string.Empty, used), res);
            refreshing = false;
            if (used != session)
            {
                yield break; // the player signed out or in again meanwhile
            }
            if (res.Status == 401 || res.Status == 403)
            {
                Drop(BlaziumSignInState.Idle);
                yield break;
            }
            string token = res.Status >= 200 && res.Status < 300 ? BlaziumJson.Field(res.Body, "token") : string.Empty;
            if (token.Length > 0 && Adopt(token, string.Empty, true))
            {
                yield break;
            }
            nextRefreshTry = Clock() + 60;
        }

        // A coroutine that renews the session for as long as the game runs. Start it once after sign-in.
        public IEnumerator KeepFresh(float everySeconds)
        {
            while (true)
            {
                yield return transport.Wait(everySeconds < 5f ? 5f : everySeconds);
                yield return Refresh(false);
            }
        }

        // ---- tokens ------------------------------------------------------------------------------------

        // The short-lived token a service wants ("sapi", "lobby" or "server"). Renews the session first
        // when it is due, and reuses a token until a minute before it ends. Never returns the session.
        public IEnumerator Get(string audience, BlaziumTokenReply reply)
        {
            string want = (audience ?? string.Empty).Trim().ToLowerInvariant();
            if (want != "sapi" && want != "lobby" && want != "server")
            {
                Refuse(reply, "bad_audience", "audience must be sapi, lobby or server");
                yield break;
            }
            if (session.Length == 0)
            {
                Refuse(reply, "not_logged_on", "sign in before asking for a token");
                yield break;
            }
            yield return Refresh(false);
            if (session.Length == 0)
            {
                Refuse(reply, "not_logged_on", "the sign-in has ended; sign in again");
                yield break;
            }
            TokenEntry held;
            if (cache.TryGetValue(want, out held) && held.Epoch == epoch && Clock() < held.Until)
            {
                NativeTokenSource.Copy(held.Reply, reply);
                yield break;
            }
            string used = session;
            BlaziumSapiResult res = new BlaziumSapiResult();
            yield return transport.Send(Json("POST", ApiBase.TrimEnd('/') + "/private/games/" + Uri.EscapeDataString(GameUid) + "/tokens", "{\"audience\":\"" + want + "\"}", used), res);
            if (res.Status == 401)
            {
                if (used == session)
                {
                    Drop(BlaziumSignInState.Idle);
                }
                Refuse(reply, "not_logged_on", "the sign-in has ended; sign in again");
                yield break;
            }
            // The service answers {"data":{"token":...}}; Field looks inside the wrapper.
            BlaziumTokenReply parsed = new BlaziumTokenReply();
            if (res.Status >= 200 && res.Status < 300)
            {
                parsed.Token = BlaziumJson.Field(res.Body, "token");
                parsed.ExpiresAt = BlaziumJson.Field(res.Body, "expires_at");
                int.TryParse(BlaziumJson.Field(res.Body, "expires_in"), out parsed.ExpiresIn);
                parsed.Ok = parsed.Token.Length > 0;
            }
            if (!parsed.Ok)
            {
                string code = res.Status == 0 ? "service_unavailable" : BlaziumJson.Field(res.Body, "code");
                Refuse(reply, code.Length > 0 ? code : "http_" + res.Status, res.Status == 0 ? "the token service could not be reached" : Refusal(res.Body));
                yield break;
            }
            parsed.Audience = want;
            parsed.GameUid = GameUid;
            NativeTokenSource.Copy(parsed, reply);
            int margin = parsed.ExpiresIn > 120 ? 60 : parsed.ExpiresIn / 2;
            TokenEntry entry = new TokenEntry();
            entry.Reply = parsed;
            entry.Until = Clock() + Math.Max(0, parsed.ExpiresIn - margin);
            entry.Epoch = epoch;
            cache[want] = entry;
        }

        public void Forget()
        {
            cache.Clear();
        }

        // ---- chat ------------------------------------------------------------------------------------

        // Signs the player in to the Blazium IRC server: asks for a ten minute password (never the session).
        // It needs a Blazium account with a username; a Steam player with no account has no chat.
        public IEnumerator GetChatAccess(BlaziumChatAccess reply)
        {
            reply.Ok = false;
            reply.Username = reply.Token = reply.Host = reply.Code = reply.Error = string.Empty;
            reply.Port = 0;
            if (session.Length == 0)
            {
                reply.Code = "not_logged_on";
                reply.Error = "sign in before asking for chat access";
                yield break;
            }
            yield return Refresh(false);
            string used = session;
            BlaziumSapiResult res = new BlaziumSapiResult();
            yield return transport.Send(Json("POST", ApiBase.TrimEnd('/') + "/private/irc/token", "{}", used), res);
            if (res.Status == 401)
            {
                if (used == session)
                {
                    Drop(BlaziumSignInState.Idle);
                }
                reply.Code = "not_logged_on";
                reply.Error = "the sign-in has ended; sign in again";
                yield break;
            }
            if (res.Status < 200 || res.Status >= 300)
            {
                reply.Code = res.Status == 0 ? "service_unavailable" : "http_" + res.Status;
                reply.Error = res.Status == 0 ? "the chat service could not be reached" : Refusal(res.Body);
                yield break;
            }
            reply.Username = BlaziumJson.Field(res.Body, "username");
            reply.Token = BlaziumJson.Field(res.Body, "token");
            reply.Host = BlaziumJson.Field(res.Body, "host");
            int.TryParse(BlaziumJson.Field(res.Body, "port"), out reply.Port);
            reply.Tls = BlaziumJson.Field(res.Body, "tls") != "false";
            reply.WebSocket = BlaziumJson.Field(res.Body, "websocket");
            reply.Ok = reply.Token.Length > 0 && reply.Host.Length > 0;
            if (!reply.Ok)
            {
                reply.Token = string.Empty;
                reply.Code = "refused";
                reply.Error = "the chat service gave an unexpected answer";
            }
        }

        // ---- who the player is ---------------------------------------------------------------------
        public IEnumerator LoadProfile()
        {
            if (session.Length == 0)
            {
                yield break;
            }
            string used = session;
            BlaziumSapiResult res = new BlaziumSapiResult();
            yield return transport.Send(Json("GET", ApiBase.TrimEnd('/') + "/private/account", string.Empty, used), res);
            if (used != session)
            {
                yield break;
            }
            if (res.Status == 401)
            {
                Drop(BlaziumSignInState.Idle);
                yield break;
            }
            if (res.Status < 200 || res.Status >= 300)
            {
                yield break;
            }
            string name = BlaziumJson.Field(res.Body, "display_name");
            if (name.Length > 0)
            {
                DisplayName = name;
            }
            Username = BlaziumJson.Field(res.Body, "username");
            Notify();
        }

        // ---- internals -----------------------------------------------------------------------------

        private const string Unusable = "that session is not usable (it has ended or is not a Blazium session)";

        // Signs in with exactly the string the service accepted, and only when the service names the
        // player the session names (or names nobody). A sign-in or sign-out meanwhile wins.
        private IEnumerator Verify(string candidate, bool save)
        {
            string user;
            long exp;
            long iat;
            if (!ReadClaims(candidate, out user, out exp, out iat) || exp <= Clock())
            {
                if (save)
                {
                    Error = Unusable;
                    Notify();
                }
                else if (store != null)
                {
                    store.Clear();
                }
                yield break;
            }
            int seen = epoch;
            BlaziumSapiResult res = new BlaziumSapiResult();
            yield return transport.Send(Json("GET", ApiBase.TrimEnd('/') + "/private/account", string.Empty, candidate), res);
            if (seen != epoch)
            {
                yield break;
            }
            bool ok = res.Status >= 200 && res.Status < 300;
            string named = ok ? BlaziumJson.Field(res.Body, "user_uid") : string.Empty;
            if (ok && named.Length == 0)
            {
                named = BlaziumJson.Field(res.Body, "uid");
            }
            if (!ok || (named.Length > 0 && named != user))
            {
                bool refused = ok || res.Status == 401 || res.Status == 403;
                if (refused && !save && store != null)
                {
                    store.Clear();
                }
                Error = res.Status == 0 ? "the Blazium service could not be reached to check the session" : "the Blazium service did not accept the session";
                Notify();
                yield break;
            }
            if (!Adopt(candidate, BlaziumJson.Field(res.Body, "display_name"), save))
            {
                yield break;
            }
            Username = BlaziumJson.Field(res.Body, "username");
            Error = string.Empty;
            SetState(BlaziumSignInState.Complete);
            yield return Refresh(false);
        }

        private bool RefreshDue(long now)
        {
            if (now >= expires || now < nextRefreshTry)
            {
                return false; // an ended session cannot renew; a failed try waits a minute
            }
            long margin = (issued > 0 && expires > issued) ? (expires - issued) / 3 : 3600;
            return now >= expires - margin;
        }

        private bool Adopt(string jwt, string displayName, bool save)
        {
            string user;
            long exp;
            long iat;
            if (!ReadClaims(jwt, out user, out exp, out iat) || exp <= Clock())
            {
                return false;
            }
            if (user != UserUid)
            {
                DisplayName = string.Empty;
                Username = string.Empty;
            }
            session = jwt;
            UserUid = user;
            expires = exp;
            issued = iat;
            if (displayName.Length > 0)
            {
                DisplayName = displayName;
            }
            epoch++;
            cache.Clear();
            if (save && store != null)
            {
                store.Save(jwt);
            }
            Notify();
            return true;
        }

        private void Drop(BlaziumSignInState next)
        {
            session = string.Empty;
            expires = 0;
            issued = 0;
            UserUid = DisplayName = Username = string.Empty;
            epoch++;
            cache.Clear();
            if (store != null)
            {
                store.Clear();
            }
            State = next;
            Notify();
        }

        private void Reset()
        {
            State = BlaziumSignInState.Idle;
            LoginUrl = UserCode = VerificationUrl = Gate = Error = string.Empty;
            UserUid = DisplayName = Username = string.Empty;
        }

        private void SetState(BlaziumSignInState next)
        {
            State = next;
            Notify();
        }

        private void Fail(int mine, string why)
        {
            if (mine != generation)
            {
                return;
            }
            Error = why;
            LoginUrl = UserCode = VerificationUrl = Gate = string.Empty;
            SetState(BlaziumSignInState.Failed);
        }

        private void Notify()
        {
            Action handler = Changed;
            if (handler != null)
            {
                handler();
            }
        }

        private static void Refuse(BlaziumTokenReply reply, string code, string message)
        {
            reply.Ok = false;
            reply.Token = string.Empty;
            reply.Code = code;
            reply.Error = message;
        }

        private static string Refusal(string body)
        {
            string message = BlaziumJson.Field(body, "error");
            if (message.Length == 0 || message[0] == '{')
            {
                message = BlaziumJson.Field(body, "message");
            }
            return message.Length > 0 ? message : "the request was refused";
        }

        private static BlaziumSapiRequest Json(string method, string url, string body, string bearer)
        {
            BlaziumSapiRequest request = new BlaziumSapiRequest();
            request.Method = method;
            request.Url = url;
            request.Body = body;
            request.Headers["Accept"] = "application/json";
            if (body.Length > 0)
            {
                request.Headers["Content-Type"] = "application/json";
            }
            if (bearer.Length > 0)
            {
                request.Headers["Authorization"] = "Bearer " + bearer;
            }
            return request;
        }

        private static string Esc(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        // Learns who a session token is for and when it ends, and turns away tokens that cannot be
        // signed sessions (alg none, no signature). It does not check the signature: only the service
        // can, so a session is never signed in until the service accepts it.
        internal static bool ReadClaims(string jwt, out string user, out long exp, out long iat)
        {
            user = string.Empty;
            exp = 0;
            iat = 0;
            if (string.IsNullOrEmpty(jwt))
            {
                return false;
            }
            string[] parts = jwt.Split('.');
            if (parts.Length != 3 || !IsBase64Url(parts[2]))
            {
                return false;
            }
            string header;
            string payload;
            if (!DecodeSegment(parts[0], out header) || !DecodeSegment(parts[1], out payload))
            {
                return false;
            }
            // A JSON escape or a second alg could hide a different algorithm from Field.
            if (header.IndexOf('\\') >= 0 || header.IndexOf("\"alg\"", StringComparison.Ordinal) != header.LastIndexOf("\"alg\"", StringComparison.Ordinal))
            {
                return false;
            }
            string alg = BlaziumJson.Field(header, "alg").Trim();
            if (alg.Length == 0 || string.Equals(alg, "none", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            long.TryParse(BlaziumJson.Field(payload, "exp"), out exp);
            long.TryParse(BlaziumJson.Field(payload, "iat"), out iat);
            user = BlaziumJson.Field(payload, "user_uid");
            if (user.Length == 0)
            {
                user = BlaziumJson.Field(payload, "uid");
            }
            return exp > 0 && user.Length > 0;
        }

        private static bool IsBase64Url(string segment)
        {
            if (segment.Length == 0)
            {
                return false;
            }
            foreach (char c in segment)
            {
                bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
                if (!ok)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool DecodeSegment(string segment, out string text)
        {
            text = string.Empty;
            if (!IsBase64Url(segment))
            {
                return false;
            }
            try
            {
                string b64 = segment.Replace('-', '+').Replace('_', '/');
                while (b64.Length % 4 != 0)
                {
                    b64 += "=";
                }
                text = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }

    // The way into the Blazium IRC server. Token is the password: a secret that works for ten minutes.
    public sealed class BlaziumChatAccess
    {
        public bool Ok;
        public string Username = string.Empty;
        public string Token = string.Empty;
        public string Host = string.Empty;
        public int Port;
        public bool Tls = true;
        public string WebSocket = string.Empty;
        public string Code = string.Empty;
        public string Error = string.Empty;

        // Never prints the password.
        public override string ToString()
        {
            return Ok ? "chat as " + Username + " on " + Host + ":" + Port : Code + ": " + Error;
        }
    }
    // What a dedicated server or backend learns about a player.
    public sealed class BlaziumPlayerProof
    {
        public bool Ok;
        public string UserUid = string.Empty;
        public string GameUid = string.Empty;
        public string Code = string.Empty;
        public string Error = string.Empty;
    }

    // A dedicated server (or any backend) asks who a player is, from the "server" token the player's game
    // sent it. It needs the game's server key. Each token works once, so a leaked one cannot be replayed.
    //
    //   // On the player's game:   session.Get("server", reply)  -> send reply.Token to the server
    //   // On the server:          yield return BlaziumPlayerVerifier.Verify(transport, serverKey, token, proof);
    public static class BlaziumPlayerVerifier
    {
        public static IEnumerator Verify(IBlaziumSapiTransport transport, string serverKey, string playerToken, BlaziumPlayerProof proof, string apiBase = BlaziumPlayerSession.DefaultApiBase)
        {
            proof.Ok = false;
            proof.UserUid = proof.GameUid = proof.Code = proof.Error = string.Empty;
            if (string.IsNullOrEmpty(serverKey) || string.IsNullOrEmpty(playerToken))
            {
                proof.Code = "bad_request";
                proof.Error = "a server key and a player token are required";
                yield break;
            }
            BlaziumSapiRequest request = new BlaziumSapiRequest();
            request.Method = "POST";
            request.Url = apiBase.TrimEnd('/') + "/server/verify-player";
            request.Body = "{\"token\":\"" + playerToken.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}";
            request.Headers["Content-Type"] = "application/json";
            request.Headers["Accept"] = "application/json";
            request.Headers["X-Server-Key"] = serverKey;
            BlaziumSapiResult res = new BlaziumSapiResult();
            yield return transport.Send(request, res);
            if (res.Status == 0)
            {
                proof.Code = "service_unavailable";
                proof.Error = "the Blazium service could not be reached";
                yield break;
            }
            string uid = BlaziumJson.Field(res.Body, "user_uid");
            if (res.Status < 200 || res.Status >= 300 || uid.Length == 0)
            {
                proof.Code = res.Status == 409 ? "already_used" : "refused";
                string message = BlaziumJson.Field(res.Body, "message");
                proof.Error = message.Length > 0 ? message : "the player token was refused";
                yield break;
            }
            proof.Ok = true;
            proof.UserUid = uid;
            proof.GameUid = BlaziumJson.Field(res.Body, "game_uid");
        }
    }
}
