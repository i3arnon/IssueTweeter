using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace IssueTweeter
{
    public static class EnumerableExtensions
    {
        public static async Task ForEachAsync<TSource>(this IEnumerable<TSource> source, Func<TSource, Task> action)
        {
            foreach (var item in source)
            {
                await action(item);
            }
        }
    }
}
