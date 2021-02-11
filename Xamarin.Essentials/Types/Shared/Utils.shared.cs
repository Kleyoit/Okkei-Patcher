using System;

namespace Xamarin.Essentials
{
    static class Utils
    {
        internal static Version ParseVersion(string version)
        {
            if (Version.TryParse(version, out var number))
                return number;

            if (int.TryParse(version, out var major))
                return new Version(major, 0);

            return new Version(0, 0);
        }
    }
}
