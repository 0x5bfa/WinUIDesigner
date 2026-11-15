// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

namespace WinUIDesigner.Markup;

/// <summary>
/// What the namespace policies decide for the members written under one <c>xmlns</c> prefix.
/// </summary>
internal enum XamlPrefixDisposition
{
    /// <summary>
    /// No policy has an opinion; the members stay exactly as written.
    /// </summary>
    Leave,

    /// <summary>
    /// The members apply to the previewed document and become plain members of their bare namespace.
    /// </summary>
    Keep,

    /// <summary>
    /// The members do not apply to the previewed document and are removed.
    /// </summary>
    Drop,
}
