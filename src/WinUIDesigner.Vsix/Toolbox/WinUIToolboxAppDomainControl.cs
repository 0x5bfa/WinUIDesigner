// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Concurrent;
using Microsoft.VisualStudio.DesignTools.DesignerContract;
using Microsoft.VisualStudio.DesignTools.DesignerContract.Isolation;
using Microsoft.VisualStudio.Shell;

namespace WinUIDesigner.Vsix.Toolbox;

public sealed class WinUIToolboxAppDomainControl : IToolboxAppDomainControl
{
    private readonly ConcurrentDictionary<int, IsolatedObjectFactory> domains = new();
    public WinUIToolboxAppDomainControl()
    {
        ToolboxTrace.Write("AppDomainControl constructed without IServiceProvider.");
    }

    public WinUIToolboxAppDomainControl(IServiceProvider services)
    {
        ToolboxTrace.Write($"AppDomainControl constructed with IServiceProvider={services?.GetType().FullName ?? "<null>"}.");
    }

    public AppDomain CreateAppDomain()
    {
        ToolboxTrace.Write("CreateAppDomain requested.");
        var factory = new IsolatedObjectFactory($"WinUIDesigner.Toolbox.{Guid.NewGuid()}",
            new ObjectCreator(typeof(IDesignTimeMetadataAppDomainInitializer),
                "Microsoft.VisualStudio.DesignTools.SurfaceDesigner",
                "Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Metadata.Assemblies.DesignTimeMetadataAppDomainInitializer"));
        try
        {
            AppDomain domain = factory.CreateInstance<IDesignTimeMetadataAppDomainInitializer>().InitializeAndGetAppDomain();
            domains[domain.Id] = factory;
            ToolboxTrace.Write($"CreateAppDomain completed: id={domain.Id}, name={domain.FriendlyName}.");
            return domain;
        }
        catch (Exception exception)
        {
            ToolboxTrace.Write($"CreateAppDomain failed: {exception}");
            factory.Dispose();
            throw;
        }
    }

    public void UnloadAppDomain(AppDomain domain)
    {
        ToolboxTrace.Write($"UnloadAppDomain requested: id={domain?.Id}, name={domain?.FriendlyName}.");
        if (domain is not null && domains.TryRemove(domain.Id, out var factory)) factory.Dispose();
    }
}
