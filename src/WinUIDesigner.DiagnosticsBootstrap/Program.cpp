#include <windows.h>
#include <combaseapi.h>

#include <cwchar>
#include <iostream>
#include <string>

namespace
{
    constexpr CLSID CLSID_WinUIDesignerDiagnosticsTap =
    { 0x985cbf93, 0x5e24, 0x4a1e, { 0xb7, 0x94, 0x9c, 0x99, 0xc5, 0xd4, 0xd8, 0xa2 } };
}

int wmain(int argc, wchar_t** argv)
{
    if (argc != 4 && argc != 5)
    {
        std::wcerr << L"Usage: WinUIDesigner.DiagnosticsBootstrap.exe <pid> <FrameworkUdk.dll> <DiagnosticsTap.dll> [tap-clsid]\n";
        return ERROR_INVALID_PARAMETER;
    }

    wchar_t* end = nullptr;
    const unsigned long pid = wcstoul(argv[1], &end, 10);
    if (pid == 0 || end == argv[1] || *end != L'\0')
    {
        std::wcerr << L"Invalid pid.\n";
        return ERROR_INVALID_PARAMETER;
    }

    HMODULE frameworkUdk = LoadLibraryW(argv[2]);
    if (frameworkUdk == nullptr)
    {
        const DWORD error = GetLastError();
        std::wcerr << L"LoadLibraryW failed: " << error << L"\n";
        return static_cast<int>(error);
    }

    using InitializeXamlDiagnosticsExFn = HRESULT(WINAPI*)(LPCWSTR, DWORD, LPCWSTR, LPCWSTR, CLSID, LPCWSTR);
    auto initialize = reinterpret_cast<InitializeXamlDiagnosticsExFn>(GetProcAddress(frameworkUdk, "InitializeXamlDiagnosticsEx"));
    if (initialize == nullptr)
    {
        const DWORD error = GetLastError();
        std::wcerr << L"GetProcAddress failed: " << error << L"\n";
        return static_cast<int>(error);
    }

    CLSID tapClsid = CLSID_WinUIDesignerDiagnosticsTap;
    if (argc == 5)
    {
        const HRESULT parseHr = CLSIDFromString(argv[4], &tapClsid);
        if (FAILED(parseHr))
        {
            std::wcerr << L"Invalid CLSID.\n";
            return ERROR_INVALID_PARAMETER;
        }
    }

    constexpr HRESULT endpointNotFound = HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
    const ULONGLONG timeout = GetTickCount64() + 60'000;
    HRESULT hr = endpointNotFound;

    for (unsigned int connection = 1; connection <= 4 && GetTickCount64() < timeout; ++connection)
    {
        std::wstring endpoint = L"WinUIVisualDiagConnection" + std::to_wstring(connection);
        do
        {
            hr = initialize(
                endpoint.c_str(),
                static_cast<DWORD>(pid),
                argv[2],
                argv[3],
                tapClsid,
                L"");

            if (SUCCEEDED(hr) || hr != endpointNotFound)
            {
                std::wcout << endpoint << L": 0x" << std::hex << static_cast<unsigned long>(hr) << L"\n";
                return FAILED(hr) ? static_cast<int>(HRESULT_CODE(hr)) : 0;
            }

            Sleep(500);
        } while (GetTickCount64() < timeout);
    }

    std::wcout << L"0x" << std::hex << static_cast<unsigned long>(hr) << L"\n";
    return FAILED(hr) ? static_cast<int>(HRESULT_CODE(hr)) : 0;
}
