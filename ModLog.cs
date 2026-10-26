using UnityEngine;

#nullable enable
namespace AzusaSdModle
{
    public static class ModLog
    {
        private const string ModName = "ModName";

        public static void Log(object message) => Debug.Log((object)$"{"AzusaSdModle"} Log: {message}");

        public static void Warning(object message)
        {
            Debug.LogWarning((object)$"{"AzusaSdModle"} Warning: {message}");
        }

        public static void Error(object message)
        {
            Debug.LogError((object)$"{"AzusaSdModle"} Error: {message}");
        }
    }
}
