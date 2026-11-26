using System;
using System.Collections;
using System.Collections.Generic;
using Blazium.Games;

namespace Blazium.Games.Tests
{
    internal sealed class FakeTransport : IBlaziumSapiTransport
    {
        public readonly List<BlaziumSapiRequest> Sent = new List<BlaziumSapiRequest>();
        public readonly List<float> Waits = new List<float>();
        public readonly Queue<BlaziumSapiResult> Replies = new Queue<BlaziumSapiResult>();

        public IEnumerator Send(BlaziumSapiRequest request, BlaziumSapiResult result)
        {
            BlaziumSapiRequest copy = new BlaziumSapiRequest();
            copy.Method = request.Method;
            copy.Url = request.Url;
            copy.Body = request.Body;
            foreach (KeyValuePair<string, string> h in request.Headers)
            {
                copy.Headers[h.Key] = h.Value;
            }
            Sent.Add(copy);
            BlaziumSapiResult reply = Replies.Count > 0 ? Replies.Dequeue() : Reply(200, "{}");
            result.Status = reply.Status;
            result.Body = reply.Body;
            result.RetryAfter = reply.RetryAfter;
            result.RequestId = reply.RequestId;
            yield break;
        }

        public IEnumerator Wait(float seconds)
        {
            Waits.Add(seconds);
            yield break;
        }

        public static BlaziumSapiResult Reply(int status, string body, string retryAfter = "")
        {
            BlaziumSapiResult r = new BlaziumSapiResult();
            r.Status = status;
            r.Body = body;
            r.RetryAfter = retryAfter;
            return r;
        }
    }

    internal sealed class FakeTokens : IBlaziumTokenSource
    {
        public int Asked;
        public int Forgotten;
        public string Audience = string.Empty;
        public bool Fail;

        public IEnumerator Get(string audience, BlaziumTokenReply reply)
        {
            Asked++;
            Audience = audience;
            if (Fail)
            {
                reply.Ok = false;
                reply.Code = "not_logged_on";
                reply.Error = "blazium launcher is not logged in";
                yield break;
            }
            reply.Ok = true;
            reply.Token = "scoped-" + Asked;
            reply.Audience = audience;
            yield break;
        }

        public void Forget()
        {
            Forgotten++;
        }
    }

    internal static class Program
    {
        private static int failures;

        internal static void Check(bool ok, string what)
        {
            if (!ok)
            {
                Console.Error.WriteLine("FAIL " + what);
                failures++;
            }
        }

        internal static void Run(IEnumerator routine)
        {
            Stack<IEnumerator> stack = new Stack<IEnumerator>();
            stack.Push(routine);
            while (stack.Count > 0)
            {
                IEnumerator top = stack.Peek();
                if (!top.MoveNext())
                {
                    stack.Pop();
                    continue;
                }
                IEnumerator inner = top.Current as IEnumerator;
                if (inner != null)
                {
                    stack.Push(inner);
                }
            }
        }

        private static BlaziumSapi Make(FakeTransport t, FakeTokens k)
        {
            BlaziumSapi sapi = new BlaziumSapi(t, k);
            sapi.GameUid = "6ba7b810-9dad-11d1-80b4-00c04fd430c8";
            return sapi;
        }

        private static int Main()
        {
            const string game = "6ba7b810-9dad-11d1-80b4-00c04fd430c8";
            Check(BlaziumSapi.ValidLogic("score_submit-2"), "valid logic");
            Check(!BlaziumSapi.ValidLogic("Score"), "uppercase refused");
            Check(!BlaziumSapi.ValidLogic("a/b"), "slash refused");
            Check(!BlaziumSapi.ValidLogic(""), "empty refused");
            Check(!BlaziumSapi.ValidLogic(new string('a', 65)), "long refused");

            // A signed-in POST carries a scoped token, a channel, and an idempotency key.
            FakeTransport t = new FakeTransport();
            FakeTokens k = new FakeTokens();
            BlaziumSapi sapi = Make(t, k);
            BlaziumSapiResult r = new BlaziumSapiResult();
            Run(sapi.Call("score_submit", "POST", "{\"score\":10}", r));
            Check(r.Success && r.Status == 200, "post ok");
            Check(t.Sent.Count == 1 && t.Sent[0].Url == "https://sapi.blazium.online/v1/" + game + "/score_submit", "url " + (t.Sent.Count > 0 ? t.Sent[0].Url : ""));
            Check(t.Sent[0].Headers["Authorization"] == "Bearer scoped-1", "bearer");
            Check(k.Audience == "sapi", "audience sapi");
            Check(t.Sent[0].Headers["X-Blazium-Channel"] == "stable", "channel");
            Check(t.Sent[0].Headers["Idempotency-Key"].Length == 32, "idempotency key");
            Check(!t.Sent[0].Url.Contains("scoped-"), "token never in the url");
            Check(!r.ToString().Contains("scoped-"), "token never in the result");

            // GET sends a query and no idempotency key.
            t = new FakeTransport(); k = new FakeTokens(); sapi = Make(t, k); r = new BlaziumSapiResult();
            Run(sapi.Call("top", "get", "limit=5", r));
            Check(t.Sent[0].Method == "GET" && t.Sent[0].Url.EndsWith("/top?limit=5"), "get url");
            Check(!t.Sent[0].Headers.ContainsKey("Idempotency-Key"), "no key on get");
            Check(t.Sent[0].Body.Length == 0, "no body on get");

            // Anonymous skips the token.
            t = new FakeTransport(); k = new FakeTokens(); sapi = Make(t, k); r = new BlaziumSapiResult();
            Run(sapi.Call("hello", "POST", "{}", r, true));
            Check(k.Asked == 0 && !t.Sent[0].Headers.ContainsKey("Authorization"), "anonymous has no token");

            // A body over 64 KiB and a bad method never leave.
            t = new FakeTransport(); k = new FakeTokens(); sapi = Make(t, k); r = new BlaziumSapiResult();
            Run(sapi.Call("big", "POST", new string('x', 64 * 1024 + 1), r));
            Check(!r.Success && r.ErrorKey == "bad_request" && t.Sent.Count == 0 && k.Asked == 0, "body cap");
            Run(sapi.Call("odd", "TRACE", "{}", r));
            Check(r.ErrorKey == "bad_request" && t.Sent.Count == 0, "bad method");
            Run(sapi.Call("Bad Name", "POST", "{}", r));
            Check(r.ErrorKey == "bad_request" && t.Sent.Count == 0, "bad logic");

            // 429 then 503 then 200: retries wait, honour Retry-After, and keep one idempotency key.
            t = new FakeTransport(); k = new FakeTokens(); sapi = Make(t, k); r = new BlaziumSapiResult();
            t.Replies.Enqueue(FakeTransport.Reply(429, "{}", "2"));
            t.Replies.Enqueue(FakeTransport.Reply(503, "{}"));
            t.Replies.Enqueue(FakeTransport.Reply(200, "{\"ok\":true}"));
            Run(sapi.Call("submit", "POST", "{}", r));
            Check(r.Success && t.Sent.Count == 3, "retried to success");
            Check(t.Waits.Count == 2 && t.Waits[0] == 2f && t.Waits[1] == 1f, "waits " + string.Join(",", t.Waits));
            Check(t.Sent[0].Headers["Idempotency-Key"] == t.Sent[2].Headers["Idempotency-Key"], "same key across retries");
            Check(BlaziumSapi.RetryDelay(0, "99") == 8f && BlaziumSapi.RetryDelay(5, "") == 8f, "retry delay is capped");

            // Retries stop after MaxRetries.
            t = new FakeTransport(); k = new FakeTokens(); sapi = Make(t, k); r = new BlaziumSapiResult();
            for (int i = 0; i < 10; i++)
            {
                t.Replies.Enqueue(FakeTransport.Reply(429, "{\"ok\":false,\"error\":{\"key\":\"rate_limited\",\"message\":\"slow down\"}}"));
            }
            Run(sapi.Call("submit", "POST", "{}", r));
            Check(!r.Success && t.Sent.Count == 4 && r.ErrorKey == "rate_limited" && r.Message == "slow down", "gave up after retries");

            // A 401 forgets the token and asks once more, and only once.
            t = new FakeTransport(); k = new FakeTokens(); sapi = Make(t, k); r = new BlaziumSapiResult();
            t.Replies.Enqueue(FakeTransport.Reply(401, "{}"));
            t.Replies.Enqueue(FakeTransport.Reply(200, "{}"));
            Run(sapi.Call("submit", "POST", "{}", r));
            Check(r.Success && k.Forgotten == 1 && k.Asked == 2 && t.Sent[1].Headers["Authorization"] == "Bearer scoped-2", "401 refresh");
            t = new FakeTransport(); k = new FakeTokens(); sapi = Make(t, k); r = new BlaziumSapiResult();
            t.Replies.Enqueue(FakeTransport.Reply(401, "{}"));
            t.Replies.Enqueue(FakeTransport.Reply(401, "{}"));
            Run(sapi.Call("submit", "POST", "{}", r));
            Check(!r.Success && r.Status == 401 && t.Sent.Count == 2 && r.ErrorKey == "http_401", "401 is not looped");

            // No token, no call.
            t = new FakeTransport(); k = new FakeTokens(); k.Fail = true; sapi = Make(t, k); r = new BlaziumSapiResult();
            Run(sapi.Call("submit", "POST", "{}", r));
            Check(!r.Success && r.ErrorKey == "not_logged_on" && t.Sent.Count == 0, "no token means no request");

            // The reply of the library parses, never prints the token, and a refusal has no token.
            BlaziumTokenReply ok = BlaziumTokenReply.Parse("{\"ok\":true,\"token\":\"abc.def\",\"audience\":\"sapi\",\"game_uid\":\"" + game + "\",\"expires_at\":\"2026-11-24T20:10:00Z\",\"expires_in\":600}");
            Check(ok.Ok && ok.Token == "abc.def" && ok.ExpiresIn == 600 && ok.Audience == "sapi" && ok.GameUid == game, "parse ok");
            Check(!ok.ToString().Contains("abc.def"), "ToString hides the token");
            BlaziumTokenReply no = BlaziumTokenReply.Parse("{\"ok\":false,\"code\":\"feature_off\",\"error\":\"The Scripted API is off for this game\"}");
            Check(!no.Ok && no.Token.Length == 0 && no.Code == "feature_off", "parse refusal");
            Check(!BlaziumTokenReply.Parse("").Ok && BlaziumTokenReply.Parse("").Code == "no_host", "empty reply");
            Check(!BlaziumTokenReply.Parse("{\"ok\":true}").Ok, "ok without a token is not ok");

            PlayerSessionTests.Run();
            SessionStoreTests.Run();

            if (failures > 0)
            {
                Console.Error.WriteLine(failures + " failure(s)");
                return 1;
            }
            Console.WriteLine("unity sapi tests passed");
            return 0;
        }
    }
}
