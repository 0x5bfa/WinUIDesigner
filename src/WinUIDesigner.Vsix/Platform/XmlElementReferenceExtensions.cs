// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.DesignTools.Markup;
using Microsoft.VisualStudio.DesignTools.Markup.Metadata;
using Microsoft.VisualStudio.DesignTools.Markup.Xaml;

namespace WinUIDesigner.Platform;

/// <summary>
/// Smooths over the differences in how Visual Studio versions expose <see cref="XmlElementReference"/>.
/// </summary>
internal static class XmlElementReferenceExtensions
{
    /// <summary>
    /// Returns the namespace <paramref name="prefix"/> is bound to where the element is written, or
    /// <see langword="null"/> when the prefix is not declared.
    /// </summary>
    /// <remarks>
    /// Visual Studio 18 implements <see cref="IXmlNamespaceResolver.GetXmlNamespace(XmlnsPrefix)"/> publicly on
    /// the element reference, and that member is called instead of this one. Visual Studio 17.14 implements it
    /// explicitly, so there the call goes through the interface.
    /// </remarks>
    public static XmlNamespace? GetXmlNamespace(this XmlElementReference element, XmlnsPrefix prefix)
    {
        return ((IXmlNamespaceResolver)element).GetXmlNamespace(prefix);
    }
}
