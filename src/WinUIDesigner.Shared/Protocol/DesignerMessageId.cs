// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

namespace WinUIDesigner.Protocol;

/// <summary>
/// Message identifiers used by the Visual Studio designer protocol.
/// Values owned by Visual Studio must stay aligned with its designer contract.
/// </summary>
internal enum DesignerMessageId : int
{
    VisualTreeMutations = 9,
    ExecuteXamlActions = 507,
    CreateSurface = 516,
    CloseDocument = 517,
    SetPanZoomTransform = 518,
    SetArtboardColors = 519,
    SetDeviceSize = 520,
    OnApplicationEvent = 521,
    GetProperties = 522,
    GetDefaultValue = 523,
    GoToState = 524,
    Storyboard = 525,
    HitTest = 526,
    SetFreezeState = 527,
    GetUnderlyingValue = 528,
    UnhandledException = 529,
    SurfaceDpiChanged = 530,
    SurfaceBoundsChanged = 531,
    GetSnapLines = 532,
    GetElementSnapData = 533,
    GetDocumentBuildingActions = 534,
    InstanceBuildingErrors = 535,
    ExecuteLookupActions = 539,
    SurfaceLayoutUpdated = 543,
    EvaluateStaticExtension = 546,
    SetSurfacePosition = 548,

    // Extension-owned message; keep its wire value stable across both processes.
    ConfigureAppResources = 6001,
}
