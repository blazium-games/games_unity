namespace Blazium.Games
{
    public static class BlaziumLobby
    {
        public static string CreateLobby(string lobbyType)
        {
            return BlaziumNative.CreateLobby(lobbyType);
        }

        public static string JoinLobby(string lobbyId)
        {
            return BlaziumNative.JoinLobby(lobbyId);
        }

        public static string LobbyStatus(string lobbyId)
        {
            return BlaziumNative.LobbyStatus(lobbyId);
        }

        public static string FetchIce(string sessionId)
        {
            return BlaziumNative.FetchIce(sessionId);
        }

        public static string LobbySeat()
        {
            return BlaziumNative.LobbySeat();
        }

        public static string LeaveLobby()
        {
            return BlaziumNative.LeaveLobby();
        }
    }
}
