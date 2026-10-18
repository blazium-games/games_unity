namespace Blazium.Games
{
    // Same values as EBlaziumAPIInitResult in blazium_api_common.h.
    public enum BlaziumInitResult
    {
        OK = 0,
        FailedGeneric = 1,
        NoHost = 2,
        VersionMismatch = 3,
        NoConfig = 4,
        InvalidConfig = 5,
        NotLoggedOn = 6,
        UnknownGame = 7,
        NotOwned = 8,
    }
}
