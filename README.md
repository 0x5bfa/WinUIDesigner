<h1 align="center">WinUI 3 Designer for Visual Studio</h1>
<p align="center">WinUI 3 designer integration for Visual Studio.</p>

![A screenshot](src/WinUIDesigner.Vsix/Resources/preview.png)

## Usage

[![Visual Studio Marketplace Version](https://vsmarketplacebadges.dev/version/0x5BFA.WinUIDesigner.svg)](https://marketplace.visualstudio.com/items?itemName=0x5BFA.WinUIDesigner)

Download and open a vsix file from Visual Studio Marketplace. WinUI 3 designer will be automatically available for WinUI 3 projects.

## Architecture

**`WinUIDesigner.DiagnosticsTap`**

A native x64 DLL loaded by the isolated WinUI surface process. It attaches to WinUI XAML Diagnostics, exposes property-value-source queries and rendering control to the managed surface, and implements the diagnostics TAP COM entry points.

**`WinUIDesigner.Surface`**

The out-of-process WinUI 3 designer surface, published as the self-contained `WinUISurface.exe`. It connects to Visual Studio through the designer's TAP/pipe contracts, builds and updates the XAML document, and handles designer operations such as selection, hit testing, property values, and layout. It calls `WinUIDesigner.DiagnosticsTap.dll` for native XAML diagnostics that are not exposed by the public WinUI API.

**`WinUIDesigner.Vsix`**

The in-process Visual Studio extension. It registers the WinUI XAML runtime, selects the WinUI platform implementation, reuses Visual Studio's designer host and process-isolation services, stages and launches `WinUISurface.exe`, and provides Toolbox integration and logging.

```mermaid
flowchart TB
    VS[Visual Studio XAML Designer] -->|loads extension| VSIX[Registration of platform, host, Toolbox, etc.<br/>&lpar;WinUIDesigner.Vsix&rpar;]
    VSIX -->|uses shared designer contracts| HOST[Visual Studio Designer Host]
    HOST -->|starts isolated surface<br/>and passes TAP/pipe data| SURFACE[WinUISurface.exe<br/>&lpar;WinUIDesigner.Surface&rpar;]
    SURFACE <-->|designer protocol over pipes| HOST
    SURFACE -->|P/Invoke| TAP[native XAML Diagnostics TAP<br/>&lpar;WinUIDesigner.DiagnosticsTap.dll&rpar;]
    TAP <-->|IXamlDiagnostics / IVisualTreeService| XAML[WinUI 3 XAML runtime]
    SURFACE -->|creates and manipulates| XAML
```
