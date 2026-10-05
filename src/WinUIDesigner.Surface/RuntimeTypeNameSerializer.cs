// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace WinUIDesigner.Surface;

// Describe runtime values using types visible in the project's reference metadata.
// CsWinRT collection views are implementation objects, not authored XAML types.
internal static class RuntimeTypeNameSerializer
{
    private static readonly Assembly CoreLibraryAssembly = typeof(object).Assembly;

    private static readonly string SystemRuntimeAssemblyFullName =
        $"System.Runtime, Version={CoreLibraryAssembly.GetName().Version}, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a";

    private static readonly Type[] CollectionContracts =
    [
        typeof(IDictionary<,>), typeof(IList<>), typeof(ICollection<>),
        typeof(IReadOnlyDictionary<,>), typeof(IReadOnlyList<>), typeof(IReadOnlyCollection<>),
        typeof(IEnumerable<>), typeof(IDictionary), typeof(IList), typeof(ICollection), typeof(IEnumerable),
    ];

    public static string Serialize(Type type)
    {
        type = GetContractType(type);

        return $"{GetTypeName(type)}, {GetAssemblyName(type)}";
    }

    private static Type GetContractType(Type type)
    {
        if (type.Assembly.GetName().Name != "WinRT.Runtime" || type.IsInterface)
            return type;

        var interfaces = type.GetInterfaces();

        foreach (Type contract in CollectionContracts)
        {
            var match = interfaces
                .Where(candidate => candidate == contract || candidate.IsGenericType && candidate.GetGenericTypeDefinition() == contract)
                .OrderBy(candidate => candidate.FullName, StringComparer.Ordinal)
                .FirstOrDefault();

            if (match is not null)
            {
                return match;
            }
        }

        return type;
    }

    private static string GetTypeName(Type type)
    {
        if (type.IsArray)
        {
            var suffix = type.IsSZArray
                ? "[]"
                : type.GetArrayRank() == 1
                    ? "[*]"
                    : "[" + new string(',', type.GetArrayRank() - 1) + "]";

            return GetTypeName(type.GetElementType()!) + suffix;
        }

        if (type.IsGenericType && !type.IsGenericTypeDefinition)
        {
            var definition = type.GetGenericTypeDefinition().FullName!;
            var arguments = string.Join(",", type.GetGenericArguments().Select(argument => $"[{Serialize(argument)}]"));

            return $"{definition}[{arguments}]";
        }

        return type.FullName ?? type.Name;
    }

    private static string GetAssemblyName(Type type)
    {
        if (type.IsArray)
        {
            return GetAssemblyName(type.GetElementType()!);
        }

        // These framework contracts live in System.Runtime in the reference pack.
        // Preserve other assemblies rather than assuming every CoreLib type is
        // exported by that facade (List/Dictionary, for example, are not).
        if (type.Assembly == CoreLibraryAssembly &&
            (type.IsPublic && !type.IsGenericType || type.IsInterface || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>)))
        {
            return SystemRuntimeAssemblyFullName;
        }

        return type.Assembly.FullName!;
    }
}
