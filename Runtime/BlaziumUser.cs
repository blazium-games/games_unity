namespace Blazium.Games
{
    public static class BlaziumUser
    {
        public static bool IsLoggedOn()
        {
            return BlaziumNative.UserLoggedOn();
        }

        public static string UserUid()
        {
            return BlaziumNative.UserUid();
        }

        public static string DisplayName()
        {
            return BlaziumNative.DisplayName();
        }

        public static string AvatarUrl()
        {
            return BlaziumNative.AvatarUrl();
        }

        public static string Username()
        {
            return BlaziumNative.Username();
        }

        public static string GameUid()
        {
            return BlaziumNative.GameUid();
        }

        public static string SignInWithSteamTicket(string ticket)
        {
            return BlaziumNative.SignInWithSteamTicket(ticket);
        }

        public static string SteamId()
        {
            return BlaziumNative.SteamId();
        }

        public static string SteamExpires()
        {
            return BlaziumNative.SteamExpires();
        }

        public static string ReportBug(string message, string clientVersion, string osName, string arch, string logText, string buildId = "", string screenshotPath = "")
        {
            return BlaziumNative.ReportBug(message, clientVersion, osName, arch, logText, buildId, screenshotPath);
        }

        public static string BugNotices()
        {
            return BlaziumNative.BugNotices();
        }

        public static string AckBugNotices(string uidsJson)
        {
            return BlaziumNative.AckBugNotices(uidsJson);
        }

        public static string Ownership(string edition = "", string listingUid = "")
        {
            return BlaziumNative.Ownership(edition, listingUid);
        }

        public static string UnlockAchievement(string slug)
        {
            return BlaziumNative.UnlockAchievement(slug);
        }

        public static string SetStat(string slug, long value)
        {
            return BlaziumNative.SetStat(slug, value);
        }

        public static string RecordEvent(string name)
        {
            return BlaziumNative.RecordEvent(name);
        }

        public static string RecordStandardEvents(string buildId, string deviceUid = "unity")
        {
            return BlaziumNative.RecordStandardEvents(buildId, deviceUid);
        }

        public static string Progress()
        {
            return BlaziumNative.Progress();
        }

        public static string SetPresence(string status)
        {
            return BlaziumNative.SetPresence(status);
        }

        public static string PatchNotices()
        {
            return BlaziumNative.PatchNotices();
        }

        public static string AckPatchNotices(string uidsJson)
        {
            return BlaziumNative.AckPatchNotices(uidsJson);
        }

        public static string TransferItem(string itemIdsJson, long itemdefId, long count, string direction, string requestId)
        {
            return BlaziumNative.TransferItem(itemIdsJson, itemdefId, count, direction, requestId);
        }

        public static string SocketItem(string parentId, string childId, long index, string requestId)
        {
            return BlaziumNative.SocketItem(parentId, childId, index, requestId);
        }

        public static string UnsocketItem(string childId, string requestId)
        {
            return BlaziumNative.UnsocketItem(childId, requestId);
        }

        public static string SetItemStats(string itemId, string statsJson, string requestId)
        {
            return BlaziumNative.SetItemStats(itemId, statsJson, requestId);
        }

        public static string UseItem(string itemId, string requestId)
        {
            return BlaziumNative.UseItem(itemId, requestId);
        }
    }
}
