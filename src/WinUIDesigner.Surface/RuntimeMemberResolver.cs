// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Linq;
using System.Reflection;

namespace WinUIDesigner.Surface;

internal static class RuntimeMemberResolver
{
    public static MethodInfo? FindMethod(Type type, string name, Type[] parameters, bool isStatic)
        => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy
            | (isStatic ? BindingFlags.Static : BindingFlags.Instance))
            .SingleOrDefault(method => method.Name == name && !method.ContainsGenericParameters
                && method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(parameters));

    public static ConstructorInfo? FindConstructor(Type type, Type[] parameters)
        => type.GetConstructors().SingleOrDefault(constructor =>
            constructor.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(parameters));
}
