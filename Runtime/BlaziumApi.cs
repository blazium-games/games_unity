using System;

namespace Blazium.Games
{
    public static class BlaziumApi
    {
        public static BlaziumInitResult Init(out string error)
        {
            if (BlaziumSession.Current == BlaziumSession.Kind.Server)
            {
                error = "Do not call client init and dedicated-server init in the same process.";
                return BlaziumInitResult.FailedGeneric;
            }
            byte[] err = new byte[BlaziumNative.ErrMsgBytes];
            BlaziumInitResult result;
            try
            {
                result = BlaziumNative.BlaziumAPI_InitFlat(err);
            }
            catch (DllNotFoundException)
            {
                error = "blazium_api64.dll or libblazium_api.so is missing";
                return BlaziumInitResult.FailedGeneric;
            }
            error = BlaziumNative.DecodeError(err);
            if (result == BlaziumInitResult.OK)
            {
                BlaziumSession.Current = BlaziumSession.Kind.Client;
            }
            return result;
        }

        public static void Shutdown()
        {
            if (BlaziumSession.Current != BlaziumSession.Kind.Client)
            {
                return;
            }
            BlaziumNative.BlaziumAPI_Shutdown();
            BlaziumSession.Current = BlaziumSession.Kind.None;
        }

        public static void RunCallbacks()
        {
            if (BlaziumSession.Current != BlaziumSession.Kind.Client)
            {
                return;
            }
            BlaziumNative.BlaziumAPI_RunCallbacks();
        }

        // Asks the host for a short-lived token for this game. audience is "sapi" or "lobby".
        // Returns JSON: {"ok":true,"token":"...","audience":"sapi","game_uid":"...","expires_at":"...","expires_in":600}
        // or {"ok":false,"code":"...","error":"..."}. The package never creates a token: the host asks blazium.games.
        // The library caches it until a minute before it expires. Treat the token as a secret.
        public static string RequestToken(string audience)
        {
            return BlaziumNative.RequestToken(audience);
        }

        // Drops cached tokens, so the next RequestToken asks again. Call it after a 401.
        public static void ForgetTokens()
        {
            BlaziumNative.ForgetTokens();
        }

        // ---- Sign-in without a launcher, through the native library --------------------------------
        // For a game whose game.blaze says "mode": "standalone" (SDK 2.2). Init succeeds with nobody
        // signed in. Call StartLogin, then poll LoginState: it carries state, login_url, user_code,
        // verification_url, gate and error. Show login_url as a link or QR code, or the short code on a
        // console. A game that cannot ship the native library signs in with BlaziumPlayerSession instead.
        public static string StartLogin()
        {
            return BlaziumNative.StartLogin();
        }

        public static string LoginState()
        {
            return BlaziumNative.LoginState();
        }

        public static string CancelLogin()
        {
            return BlaziumNative.CancelLogin();
        }

        // Ends the session and forgets any saved copy.
        public static string Logout()
        {
            return BlaziumNative.Logout();
        }

        // The session token, for a platform secure store. Treat it as a password.
        public static string ExportSession()
        {
            return BlaziumNative.ExportSession();
        }

        public static string RestoreSession(string token)
        {
            return BlaziumNative.RestoreSession(token);
        }

        // A dedicated server or backend asks who a player is, from the "server" token the player's game
        // sent it. Needs the game's server key. Returns {"ok":true,"user_uid":"..."} or {"ok":false,...}.
        public static string VerifyPlayer(string serverKey, string playerToken)
        {
            return BlaziumNative.VerifyPlayer(serverKey, playerToken);
        }
    }
}