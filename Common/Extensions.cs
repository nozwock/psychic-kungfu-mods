using System;
using System.Linq;
using System.Reflection;

namespace Common.Extensions;

internal static class ReflectionExtensions
{
    public static bool MatchMethodArguments(this MethodInfo method, Type[] argumentTypes)
    {
        var p = method.GetParameters();
        return p.Length == argumentTypes.Length
            && p.Select((param, index) => (param, index))
                .All(it => it.param.ParameterType == argumentTypes[it.index]);
    }

    public static MethodInfo GetLocalMethod(
        this Type type,
        string localMethodName,
        Type[]? argumentTypes,
        BindingFlags? bindingAttr = null
    )
    {
        return type.GetNestedTypes(BindingFlags.NonPublic)
            .SelectMany(it =>
                it.GetMethods(
                    bindingAttr
                        ?? BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic
                )
            )
            .First(m =>
            {
                if (!m.Name.Contains(localMethodName))
                    return false;
                if (argumentTypes != null)
                {
                    return MatchMethodArguments(m, argumentTypes);
                }
                return true;
            });
    }
}

internal static class StringExtensions
{
    public static string RemovePrefix(this string str, string prefix) =>
        str.StartsWith(prefix) ? str[prefix.Length..] : str;

    public static string RemoveSuffix(this string str, string suffix) =>
        str.EndsWith(suffix) ? str[..^suffix.Length] : str;
}
