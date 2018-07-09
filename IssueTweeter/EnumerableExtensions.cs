using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IssueTweeter
{
    public static class EnumerableExtensions
    {
        public static async Task<IReadOnlyCollection<TResult>> SelectManyAsync<TSource, TResult>(
            this IEnumerable<TSource> source,
            Func<TSource, Task<IReadOnlyCollection<TResult>>> projection) =>
                (await Task.WhenAll(source.Select(projection))).
                    SelectMany(_ => _).
                    ToList();
    }
}