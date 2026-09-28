using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;

namespace WinUIDesigner.Surface.Services;

internal sealed class DiagnosticsPropertySourceService
{
    private const int S_OK = 0;
    private const int E_PENDING = unchecked((int)0x8000000A);
    private const int NativeVisualStateSource = 14;
    private int initializationStarted;
    private volatile bool nativeUnavailable;

    public void StartInitialization()
    {
        if (Interlocked.Exchange(ref initializationStarted, 1) != 0)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                int hr = NativeMethods.WinUIDesignerDiagnostics_Initialize();
                Program.WriteDiagnosticTrace($"WinUI diagnostics TAP initialization completed: hr=0x{hr:X8}.");
                if (hr < 0)
                {
                    nativeUnavailable = true;
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                nativeUnavailable = true;
                Program.WriteDiagnosticTrace($"WinUI diagnostics TAP initialization unavailable: {ex}");
            }
        });
    }

    public bool TryGetPropertySource(DependencyObject target, string propertyName, out BaseValueSource valueSource)
    {
        valueSource = BaseValueSource.Unknown;
        if (nativeUnavailable)
        {
            return false;
        }

        nint inspectable = 0;
        try
        {
            inspectable = WinRT.MarshalInspectable<object>.FromManaged(target, true);
            int hr = NativeMethods.WinUIDesignerDiagnostics_GetPropertySource(inspectable, propertyName, out int nativeSource);
            if (hr == E_PENDING)
            {
                return false;
            }

            if (hr != S_OK)
            {
                if (hr < 0)
                {
                    Program.WriteDiagnosticTrace($"WinUI diagnostics property-source lookup failed: property={propertyName}, hr=0x{hr:X8}.");
                }

                return false;
            }

            if (nativeSource == NativeVisualStateSource)
            {
                // Windows XAML Diagnostics uses 14 for VisualState, while the VS protocol uses 14 for Accessibility.
                valueSource = BaseValueSource.Unknown;
                return true;
            }

            if (nativeSource is >= (int)BaseValueSource.Unknown and <= (int)BaseValueSource.Coercion)
            {
                valueSource = (BaseValueSource)nativeSource;
                return true;
            }

            Program.WriteDiagnosticTrace($"WinUI diagnostics returned an unsupported property source: property={propertyName}, source={nativeSource}.");
            return false;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            nativeUnavailable = true;
            Program.WriteDiagnosticTrace($"WinUI diagnostics property-source lookup unavailable: {ex}");
            return false;
        }
        finally
        {
            if (inspectable != 0)
            {
                WinRT.MarshalInspectable<object>.DisposeAbi(inspectable);
            }
        }
    }

    public static bool TrySetRenderingEnabled(bool enabled)
    {
        try
        {
            int hr = NativeMethods.WinUIDesignerDiagnostics_SetRenderingEnabled(enabled);
            Program.WriteDiagnosticTrace($"WinUI diagnostics rendering switch: enabled={enabled}, hr=0x{hr:X8}.");
            return hr == S_OK;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Program.WriteDiagnosticTrace($"WinUI diagnostics rendering switch unavailable: {ex}");
            return false;
        }
    }

    private static class NativeMethods
    {
        [DllImport("WinUIDesigner.DiagnosticsTap.dll")]
        internal static extern int WinUIDesignerDiagnostics_Initialize();

        [DllImport("WinUIDesigner.DiagnosticsTap.dll", CharSet = CharSet.Unicode)]
        internal static extern int WinUIDesignerDiagnostics_GetPropertySource(nint instance, string propertyName, out int valueSource);

        [DllImport("WinUIDesigner.DiagnosticsTap.dll")]
        internal static extern int WinUIDesignerDiagnostics_SetRenderingEnabled([MarshalAs(UnmanagedType.Bool)] bool enabled);
    }
}
