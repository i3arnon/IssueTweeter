using System;

namespace IssueTweeter
{
    public static class StringExtensions
    {
        public static string[] Split(
            this string value,
            string separator,
            int count = int.MaxValue,
            StringSplitOptions options = StringSplitOptions.None)
        {
            return value.Split(new[] {separator}, count, options);
        }
    }
}