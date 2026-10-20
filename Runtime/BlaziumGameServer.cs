using System;

namespace Blazium.Games
{
    public static class BlaziumGameServer
    {
        public static BlaziumInitResult Init(ushort gamePort, string version, out string error)
        {
            if (BlaziumSession.Current == BlaziumSession.Kind.Client)
            {
                error = "Do not call client init and dedicated-server init in the same process.";
                return BlaziumInitResult.FailedGeneric;
            }
            byte[] err = new byte[BlaziumNative.ErrMsgBytes];
            BlaziumInitResult result;
            try
            {
                result = BlaziumNative.BlaziumGameServer_InitFlat(gamePort, BlaziumNative.Utf8(version), err);
            }
            catch (DllNotFoundException)
            {
                error = "blazium_api64.dll or libblazium_api.so is missing";
                return BlaziumInitResult.FailedGeneric;
            }
            error = BlaziumNative.DecodeError(err);
            if (result == BlaziumInitResult.OK)
            {
                BlaziumSession.Current = BlaziumSession.Kind.Server;
            }
            return result;
        }

        public static void Shutdown()
        {
            if (BlaziumSession.Current != BlaziumSession.Kind.Server)
            {
                return;
            }
            BlaziumNative.BlaziumGameServer_Shutdown();
            BlaziumSession.Current = BlaziumSession.Kind.None;
        }

        public static void RunCallbacks()
        {
            if (BlaziumSession.Current != BlaziumSession.Kind.Server)
            {
                return;
            }
            BlaziumNative.BlaziumGameServer_RunCallbacks();
        }

        public static bool LogOn()
        {
            return BlaziumNative.ServerLogOn();
        }

        public static void LogOff()
        {
            BlaziumNative.BlaziumGameServer_LogOff();
        }

        public static bool IsLoggedOn()
        {
            return BlaziumNative.ServerLoggedOn();
        }

        public static string ServerId()
        {
            return BlaziumNative.ServerId();
        }

        public static string UnlockAchievement(string serverKey, string userUid, string slug)
        {
#if !UNITY_SERVER
            return "";
#else
            return BlaziumNative.ServerUnlockAchievement(serverKey, userUid, slug);
#endif
        }

        public static string SetStat(string serverKey, string userUid, string slug, long value)
        {
#if !UNITY_SERVER
            return "";
#else
            return BlaziumNative.ServerSetStat(serverKey, userUid, slug, value);
#endif
        }

        public static string RecordEvent(string serverKey, string userUid, string name)
        {
#if !UNITY_SERVER
            return "";
#else
            return BlaziumNative.ServerRecordEvent(serverKey, userUid, name);
#endif
        }

        public static string Progress(string serverKey, string userUid)
        {
            return BlaziumNative.ServerProgressRead(serverKey, userUid);
        }
    }
}
