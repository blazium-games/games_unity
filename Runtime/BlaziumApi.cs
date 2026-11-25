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
    }
}