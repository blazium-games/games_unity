using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Blazium.Games
{
    // P/Invoke names match games_sdk public/blazium/blazium_api_flat.h.
    internal static class BlaziumNative
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || BLAZIUM_WINDOWS
        private const string Library = "blazium_api64";
#else
        private const string Library = "blazium_api";
#endif

        internal const int ErrMsgBytes = 1024;

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern BlaziumInitResult BlaziumAPI_InitFlat(byte[] errMsg);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void BlaziumAPI_Shutdown();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void BlaziumAPI_RunCallbacks();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int BlaziumAPI_IBlaziumUser_BLoggedOn();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumAPI_IBlaziumUser_GetUserUid();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumAPI_IBlaziumUser_GetDisplayName();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumAPI_IBlaziumUser_GetAvatarUrl();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumAPI_IBlaziumUser_GetUsername();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumAPI_IBlaziumUser_GetGameUid();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern BlaziumInitResult BlaziumGameServer_InitFlat(ushort gamePort, byte[] version, byte[] errMsg);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void BlaziumGameServer_Shutdown();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void BlaziumGameServer_RunCallbacks();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int BlaziumGameServer_LogOn();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void BlaziumGameServer_LogOff();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int BlaziumGameServer_BLoggedOn();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumGameServer_GetServerId();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumAPI_CreateLobby(byte[] lobbyType);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumAPI_JoinLobby(byte[] lobbyId);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumAPI_LobbyStatus(byte[] lobbyId);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumAPI_FetchIce(byte[] sessionId);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumAPI_LobbySeat();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumAPI_LeaveLobby();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumAPI_SignInWithSteamTicket(byte[] ticket);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumAPI_GetSteamId();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumAPI_GetSteamExpires();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumAPI_ReportBug(byte[] message, byte[] clientVersion, byte[] osName, byte[] arch, byte[] logText);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumAPI_ReportInGameBug(byte[] message, byte[] clientVersion, byte[] osName, byte[] arch, byte[] logText, byte[] buildId, byte[] screenshotPath);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumAPI_BugNotices();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumAPI_AckBugNotices(byte[] uidsJson);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumAPI_Ownership(byte[] edition, byte[] listingUid);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumAPI_UnlockAchievement(byte[] slug);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumAPI_SetStat(byte[] slug, long value);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumAPI_RecordEvent(byte[] name);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumAPI_RecordStandardEvents(byte[] buildId, byte[] deviceUid);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumAPI_Progress();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumAPI_SetPresence(byte[] status);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumAPI_PatchNotices();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumAPI_AckPatchNotices(byte[] uidsJson);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumAPI_TransferItem(byte[] itemIdsJson, long itemdefId, long count, byte[] direction, byte[] requestId);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumAPI_SocketItem(byte[] parentId, byte[] childId, long index, byte[] requestId);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumAPI_UnsocketItem(byte[] childId, byte[] requestId);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumAPI_SetItemStats(byte[] itemId, byte[] statsJson, byte[] requestId);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumAPI_UseItem(byte[] itemId, byte[] requestId);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumGameServer_UnlockAchievement(byte[] serverKey, byte[] userUid, byte[] slug);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumGameServer_SetStat(byte[] serverKey, byte[] userUid, byte[] slug, long value);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumGameServer_RecordEvent(byte[] serverKey, byte[] userUid, byte[] name);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BlaziumAPI_ServerProgressRead(byte[] serverKey, byte[] userUid);

        internal static bool UserLoggedOn()
        {
            return BlaziumAPI_IBlaziumUser_BLoggedOn() != 0;
        }

        internal static string UserUid()
        {
            return Copy(BlaziumAPI_IBlaziumUser_GetUserUid());
        }

        internal static string DisplayName()
        {
            return Copy(BlaziumAPI_IBlaziumUser_GetDisplayName());
        }

        internal static string AvatarUrl()
        {
            return Copy(BlaziumAPI_IBlaziumUser_GetAvatarUrl());
        }

        internal static string Username()
        {
            return Copy(BlaziumAPI_IBlaziumUser_GetUsername());
        }

        internal static string GameUid()
        {
            return Copy(BlaziumAPI_IBlaziumUser_GetGameUid());
        }

        internal static bool ServerLogOn()
        {
            return BlaziumGameServer_LogOn() != 0;
        }

        internal static bool ServerLoggedOn()
        {
            return BlaziumGameServer_BLoggedOn() != 0;
        }

        internal static string ServerId()
        {
            return Copy(BlaziumGameServer_GetServerId());
        }

        internal static string CreateLobby(string lobbyType)
        {
            return Copy(BlaziumAPI_CreateLobby(Utf8(lobbyType)));
        }

        internal static string JoinLobby(string lobbyId)
        {
            return Copy(BlaziumAPI_JoinLobby(Utf8(lobbyId)));
        }

        internal static string LobbyStatus(string lobbyId)
        {
            return Copy(BlaziumAPI_LobbyStatus(Utf8(lobbyId)));
        }

        internal static string FetchIce(string sessionId)
        {
            return Copy(BlaziumAPI_FetchIce(Utf8(sessionId)));
        }

        internal static string LobbySeat()
        {
            return Copy(BlaziumAPI_LobbySeat());
        }

        internal static string LeaveLobby()
        {
            return Copy(BlaziumAPI_LeaveLobby());
        }

        internal static string SignInWithSteamTicket(string ticket)
        {
            return Copy(BlaziumAPI_SignInWithSteamTicket(Utf8(ticket)));
        }

        internal static string SteamId()
        {
            return Copy(BlaziumAPI_GetSteamId());
        }

        internal static string SteamExpires()
        {
            return Copy(BlaziumAPI_GetSteamExpires());
        }

        internal static string ReportBug(string message, string clientVersion, string osName, string arch, string logText, string buildId, string screenshotPath)
        {
            return Copy(BlaziumAPI_ReportInGameBug(Utf8(message), Utf8(clientVersion), Utf8(osName), Utf8(arch), Utf8(logText), Utf8(buildId), Utf8(screenshotPath)));
        }

        internal static string BugNotices()
        {
            return Copy(BlaziumAPI_BugNotices());
        }

        internal static string AckBugNotices(string uidsJson)
        {
            return Copy(BlaziumAPI_AckBugNotices(Utf8(uidsJson)));
        }

        internal static string Ownership(string edition, string listingUid)
        {
            return Copy(BlaziumAPI_Ownership(Utf8(edition), Utf8(listingUid)));
        }

        internal static string UnlockAchievement(string slug)
        {
            return Copy(BlaziumAPI_UnlockAchievement(Utf8(slug)));
        }

        internal static string SetStat(string slug, long value)
        {
            return Copy(BlaziumAPI_SetStat(Utf8(slug), value));
        }

        internal static string RecordEvent(string name)
        {
            return Copy(BlaziumAPI_RecordEvent(Utf8(name)));
        }

        internal static string RecordStandardEvents(string buildId, string deviceUid)
        {
            return Copy(BlaziumAPI_RecordStandardEvents(Utf8(buildId), Utf8(deviceUid)));
        }

        internal static string Progress()
        {
            return Copy(BlaziumAPI_Progress());
        }

        internal static string SetPresence(string status)
        {
            return Copy(BlaziumAPI_SetPresence(Utf8(status)));
        }

        internal static string PatchNotices()
        {
            return Copy(BlaziumAPI_PatchNotices());
        }

        internal static string AckPatchNotices(string uidsJson)
        {
            return Copy(BlaziumAPI_AckPatchNotices(Utf8(uidsJson)));
        }

        internal static string TransferItem(string itemIdsJson, long itemdefId, long count, string direction, string requestId)
        {
            return Copy(BlaziumAPI_TransferItem(Utf8(itemIdsJson), itemdefId, count, Utf8(direction), Utf8(requestId)));
        }

        internal static string SocketItem(string parentId, string childId, long index, string requestId)
        {
            return Copy(BlaziumAPI_SocketItem(Utf8(parentId), Utf8(childId), index, Utf8(requestId)));
        }

        internal static string UnsocketItem(string childId, string requestId)
        {
            return Copy(BlaziumAPI_UnsocketItem(Utf8(childId), Utf8(requestId)));
        }

        internal static string SetItemStats(string itemId, string statsJson, string requestId)
        {
            return Copy(BlaziumAPI_SetItemStats(Utf8(itemId), Utf8(statsJson), Utf8(requestId)));
        }

        internal static string UseItem(string itemId, string requestId)
        {
            return Copy(BlaziumAPI_UseItem(Utf8(itemId), Utf8(requestId)));
        }

        internal static string ServerUnlockAchievement(string serverKey, string userUid, string slug)
        {
            return Copy(BlaziumGameServer_UnlockAchievement(Utf8(serverKey), Utf8(userUid), Utf8(slug)));
        }

        internal static string ServerSetStat(string serverKey, string userUid, string slug, long value)
        {
            return Copy(BlaziumGameServer_SetStat(Utf8(serverKey), Utf8(userUid), Utf8(slug), value));
        }

        internal static string ServerRecordEvent(string serverKey, string userUid, string name)
        {
            return Copy(BlaziumGameServer_RecordEvent(Utf8(serverKey), Utf8(userUid), Utf8(name)));
        }

        internal static string ServerProgressRead(string serverKey, string userUid)
        {
            return Copy(BlaziumAPI_ServerProgressRead(Utf8(serverKey), Utf8(userUid)));
        }

        internal static string DecodeError(byte[] buf)
        {
            if (buf == null || buf.Length == 0)
            {
                return string.Empty;
            }
            int len = 0;
            while (len < buf.Length && buf[len] != 0)
            {
                len++;
            }
            if (len == 0)
            {
                return string.Empty;
            }
            return Encoding.UTF8.GetString(buf, 0, len);
        }

        private static string Copy(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
            {
                return string.Empty;
            }
            int len = 0;
            while (Marshal.ReadByte(ptr, len) != 0)
            {
                len++;
            }
            if (len == 0)
            {
                return string.Empty;
            }
            byte[] bytes = new byte[len];
            Marshal.Copy(ptr, bytes, 0, len);
            return Encoding.UTF8.GetString(bytes);
        }

        internal static byte[] Utf8(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return new byte[] { 0 };
            }
            byte[] raw = Encoding.UTF8.GetBytes(value);
            byte[] terminated = new byte[raw.Length + 1];
            Buffer.BlockCopy(raw, 0, terminated, 0, raw.Length);
            return terminated;
        }
    }
}
