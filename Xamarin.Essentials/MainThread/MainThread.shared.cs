using System;

namespace Xamarin.Essentials
{
    public static partial class MainThread
    {
        public static bool IsMainThread =>
            PlatformIsMainThread;

        public static void BeginInvokeOnMainThread(Action action)
        {
            if (IsMainThread)
            {
                action();
            }
            else
            {
                PlatformBeginInvokeOnMainThread(action);
            }
        }
    }
}
