#if UNITY_5_3_OR_NEWER
using UnityEngine;

namespace Blazium.Games
{
    public sealed class BlaziumTick : MonoBehaviour
    {
        private void Update()
        {
            BlaziumApi.RunCallbacks();
            BlaziumGameServer.RunCallbacks();
        }
    }
}
#endif
