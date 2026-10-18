namespace Blazium.Games
{
    internal static class BlaziumSession
    {
        internal enum Kind
        {
            None,
            Client,
            Server,
        }

        internal static Kind Current;
    }
}
