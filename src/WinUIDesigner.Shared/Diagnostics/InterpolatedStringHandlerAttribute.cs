// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

#if NETFRAMEWORK
using System;

namespace System.Runtime.CompilerServices;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
internal sealed class InterpolatedStringHandlerAttribute : Attribute
{
}
#endif
