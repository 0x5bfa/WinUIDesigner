// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.DesignTools.Markup.DocumentModel;
using Microsoft.VisualStudio.DesignTools.Markup.Metadata;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.InstanceBuilders;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.InstanceBuilders.Uwp;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.Pipeline;
using WinUIDesigner.Markup;

namespace WinUIDesigner.Platform;

internal partial class WinUIInstanceBuilderPlatform
{
    private readonly DroppedNodeBuilder _droppedNodeBuilder = new();

    /// <summary>
    /// Chooses the builder that turns <paramref name="node"/> into construction actions. An element written
    /// under a dropped prefix, such as the branch of a conditional namespace that does not apply, gets a
    /// builder that creates nothing.
    /// </summary>
    /// <remarks>
    /// Properties are filtered by the instance manager, but an element in a collection or a dictionary is not
    /// a property of its parent: the builders create it through this method and skip it when no object results.
    /// The root is exempt, because a document needs its root.
    /// </remarks>
    public override IInstanceBuilder GetInstanceBuilder(IInstanceBuilderContext context, DocumentNode node)
    {
        if (node?.Parent is not null && MemberDispositions.GetNodeDisposition(node) == XamlPrefixDisposition.Drop)
        {
            return _droppedNodeBuilder;
        }

        return base.GetInstanceBuilder(context, node!);
    }

    /// <summary>
    /// Builds nothing for a node, and nothing beneath it.
    /// </summary>
    /// <remarks>
    /// The node has no object in the surface process. Everything the pipeline may ask of its builder ends
    /// with the node marked invalid, which is how builders tell their callers that there is nothing to attach.
    /// Edits inside the node therefore change nothing in the preview.
    /// </remarks>
    private sealed class DroppedNodeBuilder : UwpInstanceBuilder
    {
        public override bool CanInstantiate(IInstanceBuilderContext context, DocumentNode node)
        {
            // Not being able to create the node would be reported as an error; creating nothing is intended.
            return true;
        }

        public override void Instantiate(IInstanceBuilderContext context, LiveNodeProxy liveNode)
        {
            liveNode.Invalidate();
        }

        public override void Initialize(IInstanceBuilderContext context, LiveNodeProxy liveNode, BuildOrder buildOrder)
        {
        }

        public override void ReferencePropertyOrChild(IInstanceBuilderContext context, DocumentNode childOrPropertyNode)
        {
            // There is no object to fetch a child from, so the path to an edited descendant ends here.
            context.GetLiveNodeProxy(childOrPropertyNode)?.Invalidate();
        }

        public override void UpdateProperty(IInstanceBuilderContext context, DocumentCompositeNode parentNode, DocumentNode oldNode, DocumentNode newNode, IProperty property)
        {
        }

        public override void UpdateEvent(IInstanceBuilderContext context, DocumentCompositeNode parentNode, DocumentNode oldNode, DocumentNode newNode, IEvent eventProperty)
        {
        }

        public override void UpdateChild(IInstanceBuilderContext context, DocumentCompositeNode parentNode, DocumentNode oldNode, DocumentNode newNode, int index)
        {
        }
    }
}
