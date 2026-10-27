using CTLife.Core;
using System.Collections.Generic;
namespace CTLife.UI
{
    internal static class JobsCatalogStrings
    {
        private static readonly IReadOnlyDictionary<string,string> English=new Dictionary<string,string>
        {
            ["timeout"]="Jobs are taking too long to load. Try again.",
        };
        public static string T(string key)=>English.TryGetValue(key,out var value)? NameTable.Apply(value) :key;
    }
}
