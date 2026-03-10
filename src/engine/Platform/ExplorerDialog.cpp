#include "ExplorerDialog.h"

#ifdef _WIN32
#define NOMINMAX
#include <windows.h>
#include <shobjidl.h>

#include <string>

namespace
{
    class ScopedComInitialization
    {
    public:
        ScopedComInitialization()
        {
            m_result = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE);
        }

        ~ScopedComInitialization()
        {
            if (SUCCEEDED(m_result))
                CoUninitialize();
        }

        bool IsUsable() const
        {
            return SUCCEEDED(m_result) || m_result == RPC_E_CHANGED_MODE;
        }

    private:
        HRESULT m_result = E_FAIL;
    };

    std::wstring Utf8ToWide(const std::string& value)
    {
        if (value.empty())
            return {};

        const int requiredLength = MultiByteToWideChar(CP_UTF8, 0, value.c_str(), -1, nullptr, 0);
        if (requiredLength <= 0)
            return {};

        std::wstring result(static_cast<std::size_t>(requiredLength), L'\0');
        MultiByteToWideChar(CP_UTF8, 0, value.c_str(), -1, result.data(), requiredLength);
        if (!result.empty() && result.back() == L'\0')
            result.pop_back();
        return result;
    }

    std::string WideToUtf8(const std::wstring& value)
    {
        if (value.empty())
            return {};

        const int requiredLength = WideCharToMultiByte(CP_UTF8, 0, value.c_str(), -1, nullptr, 0, nullptr, nullptr);
        if (requiredLength <= 0)
            return {};

        std::string result(static_cast<std::size_t>(requiredLength), '\0');
        WideCharToMultiByte(CP_UTF8, 0, value.c_str(), -1, result.data(), requiredLength, nullptr, nullptr);
        if (!result.empty() && result.back() == '\0')
            result.pop_back();
        return result;
    }

    void SetDialogTitle(IFileDialog* dialog, const std::string& title)
    {
        if (!dialog || title.empty())
            return;

        const std::wstring wideTitle = Utf8ToWide(title);
        if (!wideTitle.empty())
            dialog->SetTitle(wideTitle.c_str());
    }

    void SetDialogInitialFolder(IFileDialog* dialog, const std::string& initialPath)
    {
        if (!dialog || initialPath.empty())
            return;

        const std::wstring widePath = Utf8ToWide(initialPath);
        if (widePath.empty())
            return;

        IShellItem* shellItem = nullptr;
        const HRESULT result = SHCreateItemFromParsingName(widePath.c_str(), nullptr, IID_PPV_ARGS(&shellItem));
        if (FAILED(result) || !shellItem)
            return;

        dialog->SetDefaultFolder(shellItem);
        dialog->SetFolder(shellItem);
        shellItem->Release();
    }

    std::string GetShellItemPath(IShellItem* item)
    {
        if (!item)
            return {};

        PWSTR rawPath = nullptr;
        const HRESULT result = item->GetDisplayName(SIGDN_FILESYSPATH, &rawPath);
        if (FAILED(result) || !rawPath)
            return {};

        const std::wstring widePath(rawPath);
        CoTaskMemFree(rawPath);
        return WideToUtf8(widePath);
    }
}

std::string ExplorerDialog::PickFolder(const std::string& title, const std::string& initialPath)
{
    ScopedComInitialization comInitialization;
    if (!comInitialization.IsUsable())
        return {};

    IFileOpenDialog* dialog = nullptr;
    HRESULT result = CoCreateInstance(CLSID_FileOpenDialog, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&dialog));
    if (FAILED(result) || !dialog)
        return {};

    DWORD options = 0;
    dialog->GetOptions(&options);
    dialog->SetOptions(options | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);
    SetDialogTitle(dialog, title);
    SetDialogInitialFolder(dialog, initialPath);

    result = dialog->Show(nullptr);
    if (FAILED(result))
    {
        dialog->Release();
        return {};
    }

    IShellItem* resultItem = nullptr;
    result = dialog->GetResult(&resultItem);
    dialog->Release();

    if (FAILED(result) || !resultItem)
        return {};

    const std::string selectedPath = GetShellItemPath(resultItem);
    resultItem->Release();
    return selectedPath;
}

std::string ExplorerDialog::PickFile(const std::string& title, const std::string& initialPath)
{
    const std::vector<std::string> files = PickFiles(title, initialPath);
    return files.empty() ? std::string() : files.front();
}

std::vector<std::string> ExplorerDialog::PickFiles(const std::string& title, const std::string& initialPath)
{
    ScopedComInitialization comInitialization;
    if (!comInitialization.IsUsable())
        return {};

    IFileOpenDialog* dialog = nullptr;
    HRESULT result = CoCreateInstance(CLSID_FileOpenDialog, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&dialog));
    if (FAILED(result) || !dialog)
        return {};

    DWORD options = 0;
    dialog->GetOptions(&options);
    dialog->SetOptions(options | FOS_FORCEFILESYSTEM | FOS_FILEMUSTEXIST | FOS_PATHMUSTEXIST | FOS_ALLOWMULTISELECT);
    SetDialogTitle(dialog, title);
    SetDialogInitialFolder(dialog, initialPath);

    result = dialog->Show(nullptr);
    if (FAILED(result))
    {
        dialog->Release();
        return {};
    }

    IShellItemArray* resultItems = nullptr;
    result = dialog->GetResults(&resultItems);
    dialog->Release();

    if (FAILED(result) || !resultItems)
        return {};

    DWORD itemCount = 0;
    resultItems->GetCount(&itemCount);

    std::vector<std::string> selectedPaths;
    selectedPaths.reserve(static_cast<std::size_t>(itemCount));

    for (DWORD itemIndex = 0; itemIndex < itemCount; ++itemIndex)
    {
        IShellItem* resultItem = nullptr;
        if (SUCCEEDED(resultItems->GetItemAt(itemIndex, &resultItem)) && resultItem)
        {
            const std::string selectedPath = GetShellItemPath(resultItem);
            if (!selectedPath.empty())
                selectedPaths.push_back(selectedPath);
            resultItem->Release();
        }
    }

    resultItems->Release();
    return selectedPaths;
}

#else

std::string ExplorerDialog::PickFolder(const std::string& title, const std::string& initialPath)
{
    (void)title;
    (void)initialPath;
    return {};
}

std::string ExplorerDialog::PickFile(const std::string& title, const std::string& initialPath)
{
    (void)title;
    (void)initialPath;
    return {};
}

std::vector<std::string> ExplorerDialog::PickFiles(const std::string& title, const std::string& initialPath)
{
    (void)title;
    (void)initialPath;
    return {};
}

#endif