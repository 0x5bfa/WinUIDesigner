// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Concurrent;
using Microsoft.VisualStudio.DesignTools.DesignerContract;
using Microsoft.VisualStudio.DesignTools.DesignerContract.Isolation;
using Microsoft.VisualStudio.Shell;

namespace WinUIDesigner.Toolbox;

public sealed class WinUIToolboxAppDomainControl : IToolboxAppDomainControl
{
    private readonly ConcurrentDictionary<int, IsolatedObjectFactory> _domains = new();

    public WinUIToolboxAppDomainControl()
    {
    }

    public WinUIToolboxAppDomainControl(IServiceProvider services)
    {
    }

    public AppDomain CreateAppDomain()
    {
        var factory = new IsolatedObjectFactory($"WinUIDesigner.Toolbox.{Guid.NewGuid()}",
            new ObjectCreator(typeof(IDesignTimeMetadataAppDomainInitializer),
                "Microsoft.VisualStudio.DesignTools.SurfaceDesigner",
                "Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Metadata.Assemblies.DesignTimeMetadataAppDomainInitializer"));
        try
        {
            AppDomain domain = factory.CreateInstance<IDesignTimeMetadataAppDomainInitializer>().InitializeAndGetAppDomain();
            _domains[domain.Id] = factory;

            return domain;
        }
        catch (Exception exception)
        {
            WinUIDesignerLogger.LogError("Toolbox", "CreateAppDomain failed.", exception);

            factory.Dispose();

            throw;
        }
    }

    public void UnloadAppDomain(AppDomain domain)
    {
        if (domain is not null && _domains.TryRemove(domain.Id, out var factory))
        {
            factory.Dispose();
        }
    }
}
