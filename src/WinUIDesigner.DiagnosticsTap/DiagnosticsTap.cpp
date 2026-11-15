// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

#include <windows.h>
#include <inspectable.h>
#include <ocidl.h>
#include <strsafe.h>
#include <xamlOM.h>
#include <roapi.h>
#include <wrl/wrappers/corewrappers.h>

#include <atomic>
#include <cwchar>
#include <string>

#include <wrl/client.h>

using Microsoft::WRL::ComPtr;

namespace
{
    // Microsoft.UI.Xaml.CompositionTarget-private.idl. Layout continues while
    // presentation and animations are suspended; no DesignerMode2 gate applies.
    MIDL_INTERFACE("FBFEDA10-12A4-4AC2-86F7-857EA7A22791")
    ICompositionTargetPrivate : public IUnknown
    {
        virtual HRESULT STDMETHODCALLTYPE SuspendRendering(BOOLEAN isSuspended) = 0;
    };

    // Private CLSID used only by the WinUIDesigner surface process.
    constexpr CLSID CLSID_WinUIDesignerDiagnosticsTap =
    { 0x985cbf93, 0x5e24, 0x4a1e, { 0xb7, 0x94, 0x9c, 0x99, 0xc5, 0xd4, 0xd8, 0xa2 } };

    SRWLOCK g_siteLock = SRWLOCK_INIT;
    ComPtr<IUnknown> g_site;
    ComPtr<IXamlDiagnostics> g_xamlDiagnostics;
    ComPtr<IVisualTreeService> g_visualTreeService;
    HMODULE g_module = nullptr;

    void Trace(LPCWSTR message) noexcept
    {
        wchar_t tempPath[MAX_PATH]{};
        if (GetTempPathW(ARRAYSIZE(tempPath), tempPath) == 0)
        {
            return;
        }

        wchar_t logPath[MAX_PATH]{};
        if (FAILED(StringCchPrintfW(logPath, ARRAYSIZE(logPath), L"%sWinUIDesigner.DiagnosticsTap.log", tempPath)))
        {
            return;
        }

        HANDLE file = CreateFileW(logPath, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE)
        {
            return;
        }

        DWORD written = 0;
        WriteFile(file, message, static_cast<DWORD>(wcslen(message) * sizeof(wchar_t)), &written, nullptr);
        constexpr wchar_t newline[] = L"\r\n";
        WriteFile(file, newline, sizeof(newline) - sizeof(wchar_t), &written, nullptr);
        CloseHandle(file);
    }

    void TraceGuid(LPCWSTR prefix, REFGUID guid) noexcept
    {
        wchar_t guidText[64]{};
        StringFromGUID2(guid, guidText, ARRAYSIZE(guidText));
        wchar_t message[160]{};
        StringCchPrintfW(message, ARRAYSIZE(message), L"%s %s", prefix, guidText);
        Trace(message);
    }

    void ClearSourceInfo(SourceInfo& sourceInfo) noexcept
    {
        SysFreeString(sourceInfo.FileName);
        SysFreeString(sourceInfo.Hash);
        sourceInfo.FileName = nullptr;
        sourceInfo.Hash = nullptr;
    }

    void FreePropertyChainSources(PropertyChainSource* sources, unsigned int count) noexcept
    {
        if (sources == nullptr)
        {
            return;
        }

        for (unsigned int index = 0; index < count; ++index)
        {
            SysFreeString(sources[index].TargetType);
            SysFreeString(sources[index].Name);
            ClearSourceInfo(sources[index].SrcInfo);
        }

        CoTaskMemFree(sources);
    }

    void FreePropertyChainValues(PropertyChainValue* values, unsigned int count) noexcept
    {
        if (values == nullptr)
        {
            return;
        }

        for (unsigned int index = 0; index < count; ++index)
        {
            SysFreeString(values[index].Type);
            SysFreeString(values[index].DeclaringType);
            SysFreeString(values[index].ValueType);
            SysFreeString(values[index].ItemType);
            SysFreeString(values[index].Value);
            SysFreeString(values[index].PropertyName);
        }

        CoTaskMemFree(values);
    }

    HRESULT GetConnectedServices(ComPtr<IXamlDiagnostics>& diagnostics, ComPtr<IVisualTreeService>& visualTreeService) noexcept
    {
        AcquireSRWLockShared(&g_siteLock);
        diagnostics = g_xamlDiagnostics;
        visualTreeService = g_visualTreeService;
        ReleaseSRWLockShared(&g_siteLock);

        return diagnostics != nullptr && visualTreeService != nullptr ? S_OK : E_PENDING;
    }

    // XAML Diagnostics addresses objects by its own handles. Handle 0 is its null object.
    HRESULT GetInstanceHandle(IXamlDiagnostics* diagnostics, IInspectable* instance, InstanceHandle& handle) noexcept
    {
        handle = 0;
        return instance == nullptr ? S_OK : diagnostics->GetHandleFromIInspectable(instance, &handle);
    }

    HRESULT GetPropertyIndex(IVisualTreeService* visualTreeService, InstanceHandle instance, LPCWSTR propertyName, unsigned int& propertyIndex) noexcept
    {
        propertyIndex = 0;
        if (propertyName == nullptr)
        {
            return E_POINTER;
        }

        ComPtr<IVisualTreeService2> visualTreeService2;
        const HRESULT hr = visualTreeService->QueryInterface(IID_PPV_ARGS(&visualTreeService2));
        return FAILED(hr) ? hr : visualTreeService2->GetPropertyIndex(instance, propertyName, &propertyIndex);
    }

    // Reads the collection through its owner, as Visual Studio's own TAP does. XAML Diagnostics lists
    // the items of a collection only when it knows the collection's parent; a collection handle taken
    // directly from the object accepts insertions but rejects every removal index with E_INVALIDARG.
    HRESULT GetCollectionHandle(
        IXamlDiagnostics* diagnostics,
        IVisualTreeService* visualTreeService,
        IInspectable* owner,
        LPCWSTR collectionPropertyName,
        InstanceHandle& collectionHandle) noexcept
    {
        collectionHandle = 0;
        ComPtr<IVisualTreeService2> visualTreeService2;
        InstanceHandle ownerHandle = 0;
        unsigned int propertyIndex = 0;
        HRESULT hr = S_OK;
        if (FAILED(hr = visualTreeService->QueryInterface(IID_PPV_ARGS(&visualTreeService2)))
            || FAILED(hr = GetInstanceHandle(diagnostics, owner, ownerHandle))
            || FAILED(hr = GetPropertyIndex(visualTreeService, ownerHandle, collectionPropertyName, propertyIndex))
            || FAILED(hr = visualTreeService2->GetProperty(ownerHandle, propertyIndex, &collectionHandle)))
        {
            return hr;
        }

        return collectionHandle != 0 ? S_OK : E_UNEXPECTED;
    }

    HRESULT GetModulePath(HMODULE module, std::wstring& path)
    {
        if (module == nullptr)
        {
            return E_INVALIDARG;
        }

        std::wstring buffer(512, L'\0');
        for (;;)
        {
            const DWORD length = GetModuleFileNameW(module, buffer.data(), static_cast<DWORD>(buffer.size()));
            if (length == 0)
            {
                return HRESULT_FROM_WIN32(GetLastError());
            }

            if (length < buffer.size() - 1)
            {
                buffer.resize(length);
                path = std::move(buffer);
                return S_OK;
            }

            buffer.resize(buffer.size() * 2);
        }
    }

    HRESULT ResolveFrameworkUdk(HMODULE& module, std::wstring& path)
    {
        module = GetModuleHandleW(L"Microsoft.Internal.FrameworkUdk.dll");
        if (module == nullptr)
        {
            HMODULE xamlModule = GetModuleHandleW(L"Microsoft.UI.Xaml.dll");
            if (xamlModule != nullptr)
            {
                std::wstring xamlPath;
                HRESULT hr = GetModulePath(xamlModule, xamlPath);
                if (SUCCEEDED(hr))
                {
                    const size_t slash = xamlPath.find_last_of(L"\\/");
                    if (slash != std::wstring::npos)
                    {
                        path.assign(xamlPath, 0, slash + 1);
                        path.append(L"Microsoft.Internal.FrameworkUdk.dll");
                        module = LoadLibraryW(path.c_str());
                    }
                }
            }
        }

        if (module == nullptr)
        {
            module = LoadLibraryW(L"Microsoft.Internal.FrameworkUdk.dll");
        }

        if (module == nullptr)
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }

        return GetModulePath(module, path);
    }

    class DiagnosticsTap final : public IObjectWithSite
    {
    public:
        HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void** object) override
        {
            TraceGuid(L"DiagnosticsTap::QueryInterface", riid);
            if (object == nullptr)
            {
                return E_POINTER;
            }

            *object = nullptr;
            if (riid == IID_IUnknown || riid == IID_IObjectWithSite)
            {
                *object = static_cast<IObjectWithSite*>(this);
            }
            else
            {
                return E_NOINTERFACE;
            }

            AddRef();
            return S_OK;
        }

        ULONG STDMETHODCALLTYPE AddRef() override
        {
            return ++_referenceCount;
        }

        ULONG STDMETHODCALLTYPE Release() override
        {
            const ULONG count = --_referenceCount;
            if (count == 0)
            {
                delete this;
            }

            return count;
        }

        HRESULT STDMETHODCALLTYPE SetSite(IUnknown* site) override
        {
            Trace(L"DiagnosticsTap::SetSite enter");
            ComPtr<IXamlDiagnostics> diagnostics;
            ComPtr<IVisualTreeService> visualTreeService;
            if (site != nullptr)
            {
                HRESULT hr = site->QueryInterface(IID_PPV_ARGS(&diagnostics));
                wchar_t message[96]{};
                StringCchPrintfW(message, ARRAYSIZE(message), L"SetSite IXamlDiagnostics hr=0x%08X", static_cast<unsigned int>(hr));
                Trace(message);
                if (FAILED(hr))
                {
                    return hr;
                }

                hr = site->QueryInterface(IID_PPV_ARGS(&visualTreeService));
                StringCchPrintfW(message, ARRAYSIZE(message), L"SetSite IVisualTreeService hr=0x%08X", static_cast<unsigned int>(hr));
                Trace(message);
                if (FAILED(hr))
                {
                    return hr;
                }
            }

            AcquireSRWLockExclusive(&g_siteLock);
            g_site = site;
            g_xamlDiagnostics = std::move(diagnostics);
            g_visualTreeService = std::move(visualTreeService);
            ReleaseSRWLockExclusive(&g_siteLock);
            Trace(L"DiagnosticsTap::SetSite success");
            return S_OK;
        }

        HRESULT STDMETHODCALLTYPE GetSite(REFIID riid, void** site) override
        {
            TraceGuid(L"DiagnosticsTap::GetSite", riid);
            if (site == nullptr)
            {
                return E_POINTER;
            }

            *site = nullptr;
            ComPtr<IUnknown> currentSite;
            AcquireSRWLockShared(&g_siteLock);
            currentSite = g_site;
            ReleaseSRWLockShared(&g_siteLock);
            return currentSite != nullptr ? currentSite->QueryInterface(riid, site) : E_FAIL;
        }

    private:
        std::atomic<ULONG> _referenceCount{ 1 };
    };

    class DiagnosticsTapFactory final : public IClassFactory
    {
    public:
        HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void** object) override
        {
            if (object == nullptr)
            {
                return E_POINTER;
            }

            *object = nullptr;
            if (riid != IID_IUnknown && riid != IID_IClassFactory)
            {
                return E_NOINTERFACE;
            }

            *object = static_cast<IClassFactory*>(this);
            AddRef();
            return S_OK;
        }

        ULONG STDMETHODCALLTYPE AddRef() override
        {
            return ++_referenceCount;
        }

        ULONG STDMETHODCALLTYPE Release() override
        {
            const ULONG count = --_referenceCount;
            if (count == 0)
            {
                delete this;
            }

            return count;
        }

        HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID riid, void** object) override
        {
            TraceGuid(L"DiagnosticsTapFactory::CreateInstance", riid);
            if (outer != nullptr)
            {
                return CLASS_E_NOAGGREGATION;
            }

            auto* tap = new (std::nothrow) DiagnosticsTap();
            if (tap == nullptr)
            {
                return E_OUTOFMEMORY;
            }

            const HRESULT hr = tap->QueryInterface(riid, object);
            tap->Release();
            return hr;
        }

        HRESULT STDMETHODCALLTYPE LockServer(BOOL) override
        {
            return S_OK;
        }

    private:
        std::atomic<ULONG> _referenceCount{ 1 };
    };
}

extern "C" __declspec(dllexport) HRESULT __stdcall WinUIDesignerDiagnostics_Initialize()
{
    if (g_module == nullptr)
    {
        return E_UNEXPECTED;
    }

    HMODULE frameworkUdk = nullptr;
    std::wstring frameworkUdkPath;
    HRESULT hr = ResolveFrameworkUdk(frameworkUdk, frameworkUdkPath);
    if (FAILED(hr))
    {
        return hr;
    }

    std::wstring tapPath;
    hr = GetModulePath(g_module, tapPath);
    if (FAILED(hr))
    {
        return hr;
    }

    using InitializeXamlDiagnosticsExFn = HRESULT(WINAPI*)(LPCWSTR, DWORD, LPCWSTR, LPCWSTR, CLSID, LPCWSTR);
    auto initialize = reinterpret_cast<InitializeXamlDiagnosticsExFn>(GetProcAddress(frameworkUdk, "InitializeXamlDiagnosticsEx"));
    if (initialize == nullptr)
    {
        return HRESULT_FROM_WIN32(GetLastError());
    }

    constexpr HRESULT endpointNotFound = HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
    const DWORD processId = GetCurrentProcessId();
    const ULONGLONG timeout = GetTickCount64() + 60'000;
    HRESULT initializeResult = endpointNotFound;

    do
    {
      for (unsigned int connection = 1; connection <= 4 && GetTickCount64() < timeout; ++connection)
      {
        wchar_t endpoint[64]{};
        swprintf_s(endpoint, L"WinUIVisualDiagConnection%u", connection);

            initializeResult = initialize(
                endpoint,
                processId,
                frameworkUdkPath.c_str(),
                tapPath.c_str(),
                CLSID_WinUIDesignerDiagnosticsTap,
                L"");

            if (SUCCEEDED(initializeResult) || initializeResult != endpointNotFound)
            {
                return initializeResult;
            }

      }
      Sleep(500);
    } while (GetTickCount64() < timeout);

    return initializeResult;
}

extern "C" __declspec(dllexport) HRESULT __stdcall WinUIDesignerDiagnostics_GetPropertySources(
    IInspectable* instance,
    LPCWSTR const* propertyNames,
    unsigned int requestedPropertyCount,
    int* valueSources,
    int* found)
{
    if (instance == nullptr || (requestedPropertyCount > 0 && (propertyNames == nullptr || valueSources == nullptr || found == nullptr)))
    {
        return E_POINTER;
    }

    for (unsigned int index = 0; index < requestedPropertyCount; ++index)
    {
        valueSources[index] = BaseValueSourceUnknown;
        found[index] = 0;
    }

    ComPtr<IXamlDiagnostics> diagnostics;
    ComPtr<IVisualTreeService> visualTreeService;
    AcquireSRWLockShared(&g_siteLock);
    diagnostics = g_xamlDiagnostics;
    visualTreeService = g_visualTreeService;
    ReleaseSRWLockShared(&g_siteLock);

    if (diagnostics == nullptr || visualTreeService == nullptr)
    {
        return E_PENDING;
    }

    InstanceHandle handle = 0;
    HRESULT hr = diagnostics->GetHandleFromIInspectable(instance, &handle);
    if (FAILED(hr))
    {
        return hr;
    }

    unsigned int sourceCount = 0;
    PropertyChainSource* sources = nullptr;
    unsigned int propertyCount = 0;
    PropertyChainValue* values = nullptr;
    hr = visualTreeService->GetPropertyValuesChain(handle, &sourceCount, &sources, &propertyCount, &values);
    if (FAILED(hr))
    {
        FreePropertyChainSources(sources, sourceCount);
        FreePropertyChainValues(values, propertyCount);
        return hr;
    }

    for (unsigned int requestedIndex = 0; requestedIndex < requestedPropertyCount; ++requestedIndex)
    {
        if (propertyNames[requestedIndex] == nullptr)
        {
            continue;
        }

        for (unsigned int valueIndex = 0; valueIndex < propertyCount; ++valueIndex)
        {
            const PropertyChainValue& value = values[valueIndex];
            if (value.PropertyName == nullptr || _wcsicmp(value.PropertyName, propertyNames[requestedIndex]) != 0 || value.PropertyChainIndex >= sourceCount)
            {
                continue;
            }

            valueSources[requestedIndex] = static_cast<int>(sources[value.PropertyChainIndex].Source);
            found[requestedIndex] = 1;
            break;
        }
    }

    FreePropertyChainSources(sources, sourceCount);
    FreePropertyChainValues(values, propertyCount);
    return S_OK;
}

// The edits below go through XAML Diagnostics instead of the public WinRT API. WinUI seals a Style,
// its Setters collection and its Setters once the style is applied; the public API then rejects
// insertions and setter changes, and accepts removals without updating the elements. The framework's
// diagnostics implementation (DiagnosticsInterop) unseals the objects for the edit, rebuilds the merged
// setters of styles BasedOn the edited one, re-applies implicit styles and refreshes the elements that
// use the style; Visual Studio's XAML Hot Reload edits styles through the same collection calls.
//
// Collections are named by their owner and the full metadata name of the collection property,
// for example a Style and "Microsoft.UI.Xaml.Style.Setters".
extern "C" __declspec(dllexport) HRESULT __stdcall WinUIDesignerDiagnostics_AddChild(
    IInspectable* owner,
    LPCWSTR collectionPropertyName,
    IInspectable* child,
    unsigned int index)
{
    if (owner == nullptr || child == nullptr)
    {
        return E_POINTER;
    }

    ComPtr<IXamlDiagnostics> diagnostics;
    ComPtr<IVisualTreeService> visualTreeService;
    HRESULT hr = GetConnectedServices(diagnostics, visualTreeService);
    if (FAILED(hr))
    {
        return hr;
    }

    InstanceHandle collectionHandle = 0;
    InstanceHandle childHandle = 0;
    if (FAILED(hr = GetCollectionHandle(diagnostics.Get(), visualTreeService.Get(), owner, collectionPropertyName, collectionHandle))
        || FAILED(hr = GetInstanceHandle(diagnostics.Get(), child, childHandle)))
    {
        return hr;
    }

    return visualTreeService->AddChild(collectionHandle, childHandle, index);
}

extern "C" __declspec(dllexport) HRESULT __stdcall WinUIDesignerDiagnostics_RemoveChild(
    IInspectable* owner,
    LPCWSTR collectionPropertyName,
    unsigned int index)
{
    if (owner == nullptr)
    {
        return E_POINTER;
    }

    ComPtr<IXamlDiagnostics> diagnostics;
    ComPtr<IVisualTreeService> visualTreeService;
    HRESULT hr = GetConnectedServices(diagnostics, visualTreeService);
    if (FAILED(hr))
    {
        return hr;
    }

    InstanceHandle collectionHandle = 0;
    hr = GetCollectionHandle(diagnostics.Get(), visualTreeService.Get(), owner, collectionPropertyName, collectionHandle);
    return FAILED(hr) ? hr : visualTreeService->RemoveChild(collectionHandle, index);
}

extern "C" __declspec(dllexport) HRESULT __stdcall WinUIDesignerDiagnostics_ClearChildren(
    IInspectable* owner,
    LPCWSTR collectionPropertyName)
{
    if (owner == nullptr)
    {
        return E_POINTER;
    }

    ComPtr<IXamlDiagnostics> diagnostics;
    ComPtr<IVisualTreeService> visualTreeService;
    HRESULT hr = GetConnectedServices(diagnostics, visualTreeService);
    if (FAILED(hr))
    {
        return hr;
    }

    InstanceHandle collectionHandle = 0;
    hr = GetCollectionHandle(diagnostics.Get(), visualTreeService.Get(), owner, collectionPropertyName, collectionHandle);
    return FAILED(hr) ? hr : visualTreeService->ClearChildren(collectionHandle);
}

// propertyName is the full metadata name, for example "Microsoft.UI.Xaml.Style.BasedOn".
// A null value sets the property to null. Callers use that to remove Style.BasedOn: clearing the
// property through XAML Diagnostics does not refresh the elements that use the style.
extern "C" __declspec(dllexport) HRESULT __stdcall WinUIDesignerDiagnostics_SetProperty(
    IInspectable* instance,
    LPCWSTR propertyName,
    IInspectable* value)
{
    if (instance == nullptr)
    {
        return E_POINTER;
    }

    ComPtr<IXamlDiagnostics> diagnostics;
    ComPtr<IVisualTreeService> visualTreeService;
    HRESULT hr = GetConnectedServices(diagnostics, visualTreeService);
    if (FAILED(hr))
    {
        return hr;
    }

    InstanceHandle instanceHandle = 0;
    InstanceHandle valueHandle = 0;
    unsigned int propertyIndex = 0;
    if (FAILED(hr = GetInstanceHandle(diagnostics.Get(), instance, instanceHandle))
        || FAILED(hr = GetInstanceHandle(diagnostics.Get(), value, valueHandle))
        || FAILED(hr = GetPropertyIndex(visualTreeService.Get(), instanceHandle, propertyName, propertyIndex)))
    {
        return hr;
    }

    return visualTreeService->SetProperty(instanceHandle, valueHandle, propertyIndex);
}

// Dictionary edits through XAML Diagnostics also re-resolve the {StaticResource} references that the
// framework recorded for the key, so elements pick up a style that replaced the one they used.
extern "C" __declspec(dllexport) HRESULT __stdcall WinUIDesignerDiagnostics_AddDictionaryItem(
    IInspectable* dictionary,
    IInspectable* key,
    IInspectable* value)
{
    if (dictionary == nullptr || key == nullptr)
    {
        return E_POINTER;
    }

    ComPtr<IXamlDiagnostics> diagnostics;
    ComPtr<IVisualTreeService> visualTreeService;
    HRESULT hr = GetConnectedServices(diagnostics, visualTreeService);
    if (FAILED(hr))
    {
        return hr;
    }

    ComPtr<IVisualTreeService3> visualTreeService3;
    InstanceHandle dictionaryHandle = 0;
    InstanceHandle keyHandle = 0;
    InstanceHandle valueHandle = 0;
    if (FAILED(hr = visualTreeService.As(&visualTreeService3))
        || FAILED(hr = GetInstanceHandle(diagnostics.Get(), dictionary, dictionaryHandle))
        || FAILED(hr = GetInstanceHandle(diagnostics.Get(), key, keyHandle))
        || FAILED(hr = GetInstanceHandle(diagnostics.Get(), value, valueHandle)))
    {
        return hr;
    }

    return visualTreeService3->AddDictionaryItem(dictionaryHandle, keyHandle, valueHandle);
}

extern "C" __declspec(dllexport) HRESULT __stdcall WinUIDesignerDiagnostics_RemoveDictionaryItem(
    IInspectable* dictionary,
    IInspectable* key)
{
    if (dictionary == nullptr || key == nullptr)
    {
        return E_POINTER;
    }

    ComPtr<IXamlDiagnostics> diagnostics;
    ComPtr<IVisualTreeService> visualTreeService;
    HRESULT hr = GetConnectedServices(diagnostics, visualTreeService);
    if (FAILED(hr))
    {
        return hr;
    }

    ComPtr<IVisualTreeService3> visualTreeService3;
    InstanceHandle dictionaryHandle = 0;
    InstanceHandle keyHandle = 0;
    if (FAILED(hr = visualTreeService.As(&visualTreeService3))
        || FAILED(hr = GetInstanceHandle(diagnostics.Get(), dictionary, dictionaryHandle))
        || FAILED(hr = GetInstanceHandle(diagnostics.Get(), key, keyHandle)))
    {
        return hr;
    }

    return visualTreeService3->RemoveDictionaryItem(dictionaryHandle, keyHandle);
}

extern "C" __declspec(dllexport) HRESULT __stdcall WinUIDesignerDiagnostics_SetRenderingEnabled(BOOL enabled)
{
    ComPtr<ICompositionTargetPrivate> compositionTarget;
    const HRESULT hr = RoGetActivationFactory(
        Microsoft::WRL::Wrappers::HStringReference(L"Microsoft.UI.Xaml.Media.CompositionTarget").Get(),
        IID_PPV_ARGS(&compositionTarget));
    if (FAILED(hr))
    {
        return hr;
    }

    // Called synchronously on the surface UI thread, before rollback/actions and
    // before arming the existing one-shot composition barrier on resume.
    return compositionTarget->SuspendRendering(enabled ? FALSE : TRUE);
}

extern "C" HRESULT __stdcall DllGetClassObject(REFCLSID clsid, REFIID riid, void** object)
{
    TraceGuid(L"DllGetClassObject CLSID", clsid);
    TraceGuid(L"DllGetClassObject IID", riid);
    if (clsid != CLSID_WinUIDesignerDiagnosticsTap)
    {
        return CLASS_E_CLASSNOTAVAILABLE;
    }

    auto* factory = new (std::nothrow) DiagnosticsTapFactory();
    if (factory == nullptr)
    {
        return E_OUTOFMEMORY;
    }

    const HRESULT hr = factory->QueryInterface(riid, object);
    factory->Release();
    return hr;
}

extern "C" HRESULT __stdcall DllCanUnloadNow()
{
    return S_FALSE;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_module = instance;
        DisableThreadLibraryCalls(instance);
    }

    return TRUE;
}
