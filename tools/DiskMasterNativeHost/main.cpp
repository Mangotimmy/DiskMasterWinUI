#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <objbase.h>
#include <shellapi.h>
#include <shlwapi.h>
#include <stdint.h>
#include <stdlib.h>
#include <string>
#include <vector>

#pragma comment(lib, "user32.lib")
#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "advapi32.lib")
#pragma comment(lib, "ole32.lib")

typedef void* hostfxr_handle;

struct hostfxr_initialize_parameters
{
    size_t size;
    const wchar_t* host_path;
    const wchar_t* dotnet_root;
};

typedef int32_t(__cdecl* hostfxr_initialize_for_dotnet_command_line_fn)(
    int argc,
    const wchar_t** argv,
    const hostfxr_initialize_parameters* parameters,
    hostfxr_handle* host_context_handle);

typedef int32_t(__cdecl* hostfxr_run_app_fn)(const hostfxr_handle host_context_handle);
typedef int32_t(__cdecl* hostfxr_close_fn)(const hostfxr_handle host_context_handle);

static std::wstring GetExecutablePath()
{
    std::vector<wchar_t> buffer(32768, L'\0');
    DWORD len = GetModuleFileNameW(nullptr, buffer.data(), static_cast<DWORD>(buffer.size()));
    if (len == 0 || len >= buffer.size())
    {
        return L"";
    }
    return std::wstring(buffer.data(), len);
}

static std::wstring GetDirectoryOf(const std::wstring& fullPath)
{
    size_t pos = fullPath.find_last_of(L"\\/");
    if (pos == std::wstring::npos)
    {
        return L".";
    }
    return fullPath.substr(0, pos);
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    HRESULT hrCom = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);

    std::wstring exePath = GetExecutablePath();
    if (exePath.empty())
    {
        if (SUCCEEDED(hrCom)) CoUninitialize();
        return -1;
    }

    std::wstring appDir = GetDirectoryOf(exePath);
    SetCurrentDirectoryW(appDir.c_str());

    std::wstring hostfxrPath = appDir + L"\\hostfxr.dll";
    std::wstring appDllPath = appDir + L"\\DiskMasterWinUI.dll";

    if (GetFileAttributesW(appDllPath.c_str()) == INVALID_FILE_ATTRIBUTES)
    {
        MessageBoxW(
            nullptr,
            (L"找不到核心應用程式組件：\n" + appDllPath).c_str(),
            L"DiskMaster Pro",
            MB_OK | MB_ICONERROR);
        return -2;
    }

    HMODULE hHostfxr = LoadLibraryExW(
        hostfxrPath.c_str(),
        nullptr,
        LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);

    if (!hHostfxr)
    {
        hHostfxr = LoadLibraryW(hostfxrPath.c_str());
    }

    if (!hHostfxr)
    {
        MessageBoxW(
            nullptr,
            (L"無法載入 .NET 執行階段組件 (hostfxr.dll)：\n" + hostfxrPath).c_str(),
            L"DiskMaster Pro",
            MB_OK | MB_ICONERROR);
        return -3;
    }

    auto initFn = reinterpret_cast<hostfxr_initialize_for_dotnet_command_line_fn>(
        GetProcAddress(hHostfxr, "hostfxr_initialize_for_dotnet_command_line"));
    auto runFn = reinterpret_cast<hostfxr_run_app_fn>(
        GetProcAddress(hHostfxr, "hostfxr_run_app"));
    auto closeFn = reinterpret_cast<hostfxr_close_fn>(
        GetProcAddress(hHostfxr, "hostfxr_close"));

    if (!initFn || !runFn || !closeFn)
    {
        FreeLibrary(hHostfxr);
        MessageBoxW(
            nullptr,
            L"hostfxr.dll 匯出介面不完整，無法啟動應用程式。",
            L"DiskMaster Pro",
            MB_OK | MB_ICONERROR);
        return -4;
    }

    int rawArgc = 0;
    LPWSTR* rawArgv = CommandLineToArgvW(GetCommandLineW(), &rawArgc);

    std::vector<const wchar_t*> dotnetArgv;
    dotnetArgv.push_back(appDllPath.c_str());
    if (rawArgv && rawArgc > 1)
    {
        for (int i = 1; i < rawArgc; ++i)
        {
            dotnetArgv.push_back(rawArgv[i]);
        }
    }

    hostfxr_initialize_parameters params{};
    params.size = sizeof(hostfxr_initialize_parameters);
    params.host_path = exePath.c_str();
    params.dotnet_root = appDir.c_str();

    hostfxr_handle cxt = nullptr;
    int32_t rc = initFn(
        static_cast<int>(dotnetArgv.size()),
        dotnetArgv.data(),
        &params,
        &cxt);

    if (rc != 0 || cxt == nullptr)
    {
        if (closeFn && cxt) closeFn(cxt);
        if (rawArgv) LocalFree(rawArgv);
        FreeLibrary(hHostfxr);
        return rc;
    }

    int32_t exitCode = runFn(cxt);
    closeFn(cxt);

    if (rawArgv) LocalFree(rawArgv);
    FreeLibrary(hHostfxr);
    if (SUCCEEDED(hrCom)) CoUninitialize();
    return exitCode;
}
