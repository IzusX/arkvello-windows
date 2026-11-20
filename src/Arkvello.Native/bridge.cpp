#include <windows.h>
#include <shobjidl.h>
#include <sherrors.h>
#include <algorithm>
#include <atomic>
#include <condition_variable>
#include <memory>
#include <mutex>
#include <vector>
#include "bridge.h"
#include "../../CPP/Common/MyInitGuid.h"
#include "../../CPP/Windows/FileDir.h"
#include "../../CPP/Windows/FileName.h"
#include "../../CPP/Windows/Handle.h"
#include "../../CPP/Windows/PropVariant.h"
#include "../../CPP/Windows/System.h"
#include "../../CPP/7zip/UI/Common/ArchiveExtractCallback.h"
#include "../../CPP/7zip/UI/Agent/Agent.h"
#include "../../CPP/7zip/UI/Common/PropIDUtils.h"
#include "../../CPP/7zip/UI/Common/Update.h"
#include "../../CPP/7zip/UI/FileManager/FSFolder.h"
#include "../../CPP/7zip/Common/FilePathAutoRename.h"
#include "../../CPP/7zip/Common/LimitedStreams.h"
#include "../../CPP/7zip/Compress/CopyCoder.h"

HINSTANCE g_hInstance = nullptr;
// Bounded extraction of CFsFolderStat::Enumerate from upstream FSFolder.cpp.
// Keeping this provider utility avoids linking unrelated drive/network/comment
// providers into the archive service. Recheck against the pinned source on updates.
HRESULT NFsFolder::CFsFolderStat::Enumerate() {
  if (Progress) { RINOK(Progress->SetCompleted(nullptr)) }
  Path.Add_PathSepar();
  const unsigned len = Path.Len();
  NWindows::NFile::NFind::CEnumerator enumerator;
  enumerator.SetDirPrefix(Path);
  NWindows::NFile::NFind::CDirEntry fi;
  while (enumerator.Next(fi)) {
    if (fi.Attrib & FILE_ATTRIBUTE_REPARSE_POINT) return HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED);
    if (fi.IsDir()) {
      NumFolders++; Path.DeleteFrom(len); Path += fi.Name;
      RINOK(Enumerate())
    } else { NumFiles++; Size += fi.Size; }
  }
  return S_OK;
}
// Agent exposes comparison methods through its folder vtable. The slice's
// folder is used only for native-index mapping, never for UI sorting. Avoid the
// original PanelSort dependency and its application-global settings/window.
int CompareFileNames_ForFolderList(const wchar_t *a, const wchar_t *b) {
  return CompareStringOrdinal(a, -1, b, -1, TRUE) - CSTR_EQUAL;
}
BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
  if (reason == DLL_PROCESS_ATTACH) { g_hInstance = instance; DisableThreadLibraryCalls(instance); }
  return TRUE;
}

namespace {
struct Job {
  AvCallback callback;
  void *context;
  std::atomic<bool> cancelled{false}, paused{false};
  std::mutex mutex, callbackMutex;
  std::condition_variable wake;
  std::atomic<UInt64> total{0};
  UInt64 memoryLimit = (UInt64)(Int64)-1;
  HRESULT emit(AvEvent e, wchar_t *reply = nullptr, uint32_t capacity = 0, int *answer = nullptr) {
    if (cancelled) return E_ABORT;
    std::lock_guard<std::mutex> lock(callbackMutex);
    int value = callback ? callback(context, &e, reply, capacity) : -1;
    if (answer) *answer = value;
    if (value < 0) { cancelled = true; return E_ABORT; }
    return cancelled ? E_ABORT : S_OK;
  }
  HRESULT check() {
    if (cancelled) return E_ABORT;
    if (paused) {
      AvEvent e{}; e.kind = 7;
      RINOK(emit(e))
      std::unique_lock<std::mutex> lock(mutex);
      wake.wait(lock, [&] { return !paused || cancelled; });
    }
    return cancelled ? E_ABORT : S_OK;
  }
  HRESULT progress(const UInt64 *value) {
    RINOK(check())
    AvEvent e{}; e.kind = 2; e.total = total; e.completed = value ? *value : 0;
    return emit(e);
  }
  HRESULT password(BSTR *password) {
    wchar_t reply[4096]{};
    AvEvent e{}; e.kind = 3;
    HRESULT h = emit(e, reply, 4096);
    if (h == S_OK) {
      reply[4095] = 0;
      *password = SysAllocString(reply);
      if (!*password) h = E_OUTOFMEMORY;
    }
    SecureZeroMemory(reply, sizeof(reply));
    return h;
  }
  HRESULT memory(UInt32 flags, UInt32 indexType, UInt32 index, const wchar_t *path,
    UInt64 required, UInt64 *allowed, UInt32 *answer) {
    RINOK(check())
    if (flags & NRequestMemoryUseFlags::k_IsReport) {
      AvEvent e{}; e.kind = 5; e.code = 0x101; e.path1 = path;
      e.flags = flags; e.size1 = required; e.size2 = *allowed;
      return emit(e);
    }
    if (memoryLimit != (UInt64)(Int64)-1) *allowed = memoryLimit;
    if (required <= *allowed) { *answer = NRequestMemoryAnswerFlags::k_Allow; return S_OK; }
    AvEvent e{}; e.kind = 8; e.flags = flags; e.code = indexType;
    e.index = index; e.path1 = path; e.size1 = required; e.size2 = *allowed;
    int response = 0;
    RINOK(emit(e, nullptr, 0, &response))
    if (response == 1) { *allowed = required; *answer = NRequestMemoryAnswerFlags::k_Allow; return S_OK; }
    if (response != 4 || !(flags & NRequestMemoryUseFlags::k_SkipArc_IsExpected)) return E_ABORT;
    *answer = NRequestMemoryAnswerFlags::k_SkipArc | NRequestMemoryAnswerFlags::k_Limit_Exceeded;
    e.kind = 5; e.code = 0x101;
    return emit(e); // A skipped archive must not become clean success.
  }
};

// Adapted from CExtractCallbackImp::AskWrite. Keep upstream overwrite answers,
// AutoRenamePath, and the provider's CopyFileEx/MoveFileWithProgress data path.
// No File Manager controls, registry preferences or window globals are reused.
class FileCopyCallback final : public IFolderOperationsExtractCallback, public CMyUnknownImp {
  Z7_COM_QI_BEGIN2(IFolderOperationsExtractCallback)
  Z7_COM_QI_ENTRY(IProgress)
  Z7_COM_QI_END
  Z7_COM_ADDREF_RELEASE
  Job &job;
  int overwrite = 0; // ask, overwrite all, skip all, rename all
public:
  explicit FileCopyCallback(Job &value) : job(value) {}
  Z7_COM7F_IMF(SetTotal(UInt64 value)) { job.total = value; return job.check(); }
  Z7_COM7F_IMF(SetCompleted(const UInt64 *value)) { return job.progress(value); }
  Z7_COM7F_IMF(SetNumFiles(UInt64)) { return job.check(); }
  Z7_COM7F_IMF(SetCurrentFilePath(const wchar_t *path)) {
    RINOK(job.check())
    AvEvent e{}; e.kind = 6; e.path1 = path; return job.emit(e);
  }
  Z7_COM7F_IMF(ShowMessage(const wchar_t *message)) {
    AvEvent e{}; e.kind = 5; e.path1 = message; return job.emit(e);
  }
  Z7_COM7F_IMF(AskWrite(const wchar_t *source, Int32 isFolder, const FILETIME *time,
    const UInt64 *size, const wchar_t *destination, BSTR *output, Int32 *write)) {
    RINOK(job.check())
    *output = nullptr; *write = 0;
    FString target = destination;
    NWindows::NFile::NFind::CFileInfo existing;
    if (existing.Find(target)) {
      if (existing.Attrib & FILE_ATTRIBUTE_REPARSE_POINT) return HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED);
      if (isFolder || existing.IsDir()) {
        if ((isFolder != 0) == existing.IsDir()) return S_OK;
        RINOK(ShowMessage(L"Cannot replace a folder with a file or a file with a folder."))
        return E_FAIL;
      }
      if (overwrite == 2) return S_OK;
      if (overwrite == 0) {
        AvEvent e{}; e.kind = 4; e.path1 = destination; e.path2 = source;
        e.flags = 1 | 4; e.size1 = existing.Size;
        e.time1 = (UInt64(existing.MTime.dwHighDateTime) << 32) | existing.MTime.dwLowDateTime;
        if (size) { e.flags |= 2; e.size2 = *size; }
        if (time) { e.flags |= 8; e.time2 = (UInt64(time->dwHighDateTime) << 32) | time->dwLowDateTime; }
        int answer = 5; RINOK(job.emit(e, nullptr, 0, &answer))
        switch (answer) {
          case 0: break;
          case 1: overwrite = 1; break;
          case 2: return S_OK;
          case 3: overwrite = 2; return S_OK;
          case 4: overwrite = 3; break;
          case 5: return E_ABORT;
          default: return E_INVALIDARG;
        }
      }
      if (overwrite == 3) {
        if (!AutoRenamePath(target)) return HRESULT_FROM_WIN32(ERROR_FILE_EXISTS);
      } else if (NWindows::NFile::NFind::DoesFileExist_Raw(target)) {
        // This matches the original provider's replacement policy. Cancellation
        // is not rollback; the operation UI explains partial output explicitly.
        if (!NWindows::NFile::NDir::DeleteFileAlways(target)) return HRESULT_FROM_WIN32(GetLastError());
      }
    }
    *output = SysAllocString(target);
    if (!*output) return E_OUTOFMEMORY;
    *write = 1; return S_OK;
  }
};

class DeleteSink final : public IFileOperationProgressSink, public CMyUnknownImp {
  // Windows Shell uses its own COM IIDs, rather than upstream's custom IID map.
public:
  STDMETHOD(QueryInterface)(REFIID iid, void **out) override {
    if (!out) return E_POINTER;
    *out = nullptr;
    if (iid == __uuidof(IUnknown) || iid == __uuidof(IFileOperationProgressSink))
      { *out = static_cast<IFileOperationProgressSink *>(this); AddRef(); return S_OK; }
    return E_NOINTERFACE;
  }
  Z7_COM_ADDREF_RELEASE
public:
  Job &job;
  HRESULT failure = S_OK;
  explicit DeleteSink(Job &value) : job(value) {}
  HRESULT post(HRESULT h) { if (FAILED(h) && SUCCEEDED(failure)) failure = h; return S_OK; }
  STDMETHOD(StartOperations)() override { return job.check(); }
  STDMETHOD(FinishOperations)(HRESULT h) override { return post(h); }
  STDMETHOD(PreRenameItem)(DWORD, IShellItem *, LPCWSTR) override { return job.check(); }
  STDMETHOD(PostRenameItem)(DWORD, IShellItem *, LPCWSTR, HRESULT h, IShellItem *) override { return post(h); }
  STDMETHOD(PreMoveItem)(DWORD, IShellItem *, IShellItem *, LPCWSTR) override { return job.check(); }
  STDMETHOD(PostMoveItem)(DWORD, IShellItem *, IShellItem *, LPCWSTR, HRESULT h, IShellItem *) override { return post(h); }
  STDMETHOD(PreCopyItem)(DWORD, IShellItem *, IShellItem *, LPCWSTR) override { return job.check(); }
  STDMETHOD(PostCopyItem)(DWORD, IShellItem *, IShellItem *, LPCWSTR, HRESULT h, IShellItem *) override { return post(h); }
  STDMETHOD(PreDeleteItem)(DWORD, IShellItem *) override { return job.check(); }
  STDMETHOD(PostDeleteItem)(DWORD, IShellItem *, HRESULT h, IShellItem *) override { return post(h); }
  STDMETHOD(PreNewItem)(DWORD, IShellItem *, LPCWSTR) override { return job.check(); }
  STDMETHOD(PostNewItem)(DWORD, IShellItem *, LPCWSTR, LPCWSTR, DWORD, HRESULT h, IShellItem *) override { return post(h); }
  STDMETHOD(UpdateProgress)(UINT total, UINT done) override { job.total = total; UInt64 value = done; return job.progress(&value); }
  STDMETHOD(ResetTimer)() override { return S_OK; }
  STDMETHOD(PauseTimer)() override { return S_OK; }
  STDMETHOD(ResumeTimer)() override { return S_OK; }
};

struct Session {
  CCodecs *codecs = new CCodecs;
  CMyComPtr<IUnknown> codecsOwner;
  CAgent *agent = new CAgent(codecs);
  CMyComPtr<IInFolderArchive> agentOwner;
  CArchiveLink &link;
  CMyComPtr<IInStream> memoryStream;
  bool virtualInput = false;
  Session() : codecsOwner(static_cast<ICompressCodecsInfo *>(codecs)),
    agentOwner(agent), link(agent->_archiveLink) {}
  ~Session() { link.Release(); agentOwner.Release(); memoryStream.Release(); codecs->CloseLibs(); }
};

class OpenCallback final : public IOpenCallbackUI {
  Job &job;
public:
  explicit OpenCallback(Job &value) : job(value) {}
  HRESULT Open_CheckBreak() override { return job.check(); }
  HRESULT Open_SetTotal(const UInt64 *, const UInt64 *bytes) override { job.total = bytes ? *bytes : 0; return job.check(); }
  HRESULT Open_SetCompleted(const UInt64 *, const UInt64 *bytes) override { return job.progress(bytes); }
  HRESULT Open_Finished() override { return job.check(); }
  HRESULT Open_CryptoGetTextPassword(BSTR *password) override { return job.password(password); }
};

HRESULT LoadAdjacentCodecs(CCodecs &codecs) {
  wchar_t module[32768];
  DWORD len = GetModuleFileNameW(g_hInstance, module, 32768);
  if (!len || len >= 32768) return E_FAIL;
  std::wstring dll(module, len);
  dll.resize(dll.find_last_of(L"\\/") + 1); dll += L"7z.dll";
  bool loaded = false;
  RINOK(codecs.LoadDll(dll.c_str(), false, &loaded))
  if (!loaded) return HRESULT_FROM_WIN32(ERROR_MOD_NOT_FOUND);
  codecs.NeedSetLibCodecs = false;
  return S_OK;
}

class WriteCallback final : public IUpdateCallbackUI2, public IOpenCallbackUI {
  Job &job;
  const wchar_t *initialPassword;
  CMyComBSTR_Wipe openPassword;
  const UStringVector &removablePaths;
  const CBoolVector &removableDirectories;
  std::vector<std::unique_ptr<NWindows::CHandle>> cleanupParents;
  bool publishedBeforeDeleting = false;
  HRESULT ConfirmPublished() {
    RINOK(job.check())
    if (publishedBeforeDeleting) return S_OK;
    AvEvent e{}; e.kind = 12;
    int answer = 0;
    RINOK(job.emit(e, nullptr, 0, &answer))
    if (answer != 1) return E_FAIL;
    publishedBeforeDeleting = true;
    return S_OK;
  }
  HRESULT Path(const wchar_t *path) {
    RINOK(job.check())
    AvEvent e{}; e.kind = 6; e.path1 = path; return job.emit(e);
  }
  HRESULT FileError(const FString &path, DWORD error) {
    AvEvent e{}; e.kind = 5; e.code = error; e.flags = 0x20000; e.path1 = path;
    RINOK(job.emit(e))
    return HRESULT_FROM_WIN32(error ? error : ERROR_READ_FAULT);
  }
  HRESULT PinCleanupParents(const FString &path) {
    cleanupParents.clear();
    const unsigned rootLength = NWindows::NFile::NName::GetRootPrefixSize(path);
    if (!rootLength) return FileError(path, ERROR_INVALID_PARAMETER);
    // Pin ancestors in root-to-leaf order, skipping the volume/share root.
    // Preserve the handles until the upstream delete call has returned.
    for (unsigned i = rootLength; i < path.Len(); ++i) {
      if (path[i] != L'\\' && path[i] != L'/') continue;
      FString ancestor = path;
      ancestor.DeleteFrom(i);
      UString extended;
      NWindows::NFile::NName::GetSuperPath(ancestor, extended, false);
      auto handle = std::make_unique<NWindows::CHandle>();
      HANDLE value = CreateFileW(extended.IsEmpty() ? ancestor.Ptr() : extended.Ptr(), FILE_READ_ATTRIBUTES,
        FILE_SHARE_READ, nullptr, OPEN_EXISTING,
        FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
      if (value == INVALID_HANDLE_VALUE) return FileError(ancestor, GetLastError());
      handle->Attach(value);
      BY_HANDLE_FILE_INFORMATION info{};
      if (!GetFileInformationByHandle(value, &info)) return FileError(ancestor, GetLastError());
      if (!(info.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) || (info.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT))
        return FileError(ancestor, ERROR_NOT_SUPPORTED);
      cleanupParents.push_back(std::move(handle));
    }
    return S_OK;
  }
public:
  WriteCallback(Job &value, const wchar_t *password, const UStringVector &paths, const CBoolVector &directories)
    : job(value), initialPassword(password), removablePaths(paths), removableDirectories(directories) {}
  HRESULT Open_CheckBreak() override { return job.check(); }
  HRESULT Open_SetTotal(const UInt64 *, const UInt64 *bytes) override { return SetTotal(bytes ? *bytes : 0); }
  HRESULT Open_SetCompleted(const UInt64 *, const UInt64 *bytes) override { return SetCompleted(bytes); }
  HRESULT Open_Finished() override { return job.check(); }
  HRESULT Open_CryptoGetTextPassword(BSTR *password) override {
    if (initialPassword) return CryptoGetTextPassword(password);
    RINOK(job.password(password))
    openPassword.Wipe_and_Free();
    *(&openPassword) = SysAllocString(*password);
    return (const wchar_t *)openPassword ? S_OK : E_OUTOFMEMORY;
  }
  HRESULT CryptoGetTextPassword2(Int32 *defined, BSTR *password) override {
    RINOK(job.check())
    const wchar_t *value = initialPassword ? initialPassword : (const wchar_t *)openPassword;
    *defined = value != nullptr;
    *password = value ? SysAllocString(value) : nullptr;
    return value && !*password ? E_OUTOFMEMORY : S_OK;
  }
  HRESULT CryptoGetTextPassword(BSTR *password) override {
    Int32 defined = 0;
    RINOK(CryptoGetTextPassword2(&defined, password))
    return defined ? S_OK : job.password(password);
  }
  HRESULT OpenResult(const CCodecs *, const CArchiveLink &link, const wchar_t *name, HRESULT result) override {
    if (result != S_OK) return result;
    for (unsigned i = 0; i < link.Arcs.Size(); ++i) {
      const auto &info = link.Arcs[i].ErrorInfo;
      if (info.ErrorFlags || info.WarningFlags) {
        AvEvent e{}; e.kind = 5; e.code = 0x100; e.path1 = name;
        e.size1 = info.ErrorFlags; e.size2 = info.WarningFlags;
        RINOK(job.emit(e))
        return E_FAIL;
      }
    }
    return job.check();
  }
  HRESULT StartScanning() override { return SetTotal(0); }
  HRESULT FinishScanning(const CDirItemsStat &) override { return job.check(); }
  HRESULT StartOpenArchive(const wchar_t *path) override { return Path(path); }
  HRESULT StartArchive(const wchar_t *path, bool) override { return Path(path); }
  HRESULT FinishArchive(const CFinishArchiveStat &) override { return job.check(); }
  HRESULT DeletingAfterArchiving(const FString &path, bool isDir) override {
    bool selected = false;
    for (unsigned i = 0; i < removablePaths.Size(); ++i) {
      const auto &source = removablePaths[i];
      const bool directory = removableDirectories[i];
      if ((directory == isDir && path.Compare(source) == 0) ||
          (directory && path.Len() > source.Len() && path[source.Len()] == L'\\' &&
           wmemcmp(path.Ptr(), source.Ptr(), source.Len()) == 0)) { selected = true; break; }
    }
    if (!selected) return FileError(path, ERROR_INVALID_PARAMETER);
    RINOK(PinCleanupParents(path))
    NWindows::NFile::NFind::CFileInfo info;
    if (!info.Find(path, false)) return FileError(path, GetLastError());
    if (info.IsDir() != isDir || (info.Attrib & FILE_ATTRIBUTE_REPARSE_POINT))
      return FileError(path, ERROR_NOT_SUPPORTED);
    RINOK(ConfirmPublished())
    return Path(path);
  }
  HRESULT FinishDeletingAfterArchiving() override { return ConfirmPublished(); }
  HRESULT MoveArc_Start(const wchar_t *, const wchar_t *path, UInt64, Int32) override { return Path(path); }
  HRESULT MoveArc_Progress(UInt64 total, UInt64 current) override { job.total = total; return job.progress(&current); }
  HRESULT MoveArc_Finish() override { return job.check(); }
  HRESULT ScanError(const FString &path, DWORD error) override { return FileError(path, error); }
  HRESULT ScanProgress(const CDirItemsStat &, const FString &path, bool) override { return Path(path); }
  HRESULT WriteSfx(const wchar_t *path, UInt64) override { return Path(path); }
  HRESULT SetTotal(UInt64 size) override { job.total = size; return job.check(); }
  HRESULT SetCompleted(const UInt64 *value) override { return job.progress(value); }
  HRESULT SetRatioInfo(const UInt64 *, const UInt64 *) override { return job.check(); }
  HRESULT CheckBreak() override { return job.check(); }
  HRESULT SetNumItems(const CArcToDoStat &) override { return job.check(); }
  HRESULT GetStream(const wchar_t *path, bool, bool, UInt32) override { return Path(path); }
  HRESULT OpenFileError(const FString &path, DWORD error) override { return FileError(path, error); }
  HRESULT ReadingFileError(const FString &path, DWORD error) override { return FileError(path, error); }
  HRESULT SetOperationResult(Int32 result) override { return result == 0 ? job.check() : E_FAIL; }
  HRESULT ReportExtractResult(Int32 result, Int32 encrypted, const wchar_t *path) override {
    if (result == 0) return job.check();
    AvEvent e{}; e.kind = 5; e.code = (uint32_t)result; e.flags = (uint32_t)encrypted | 0x10000u; e.path1 = path;
    RINOK(job.emit(e))
    return E_FAIL;
  }
  HRESULT ReportUpdateOperation(UInt32, const wchar_t *path, bool) override { return Path(path); }
  HRESULT ShowDeleteFile(const wchar_t *path, bool) override { return Path(path); }
};

class ExtractCallback final : public IFolderArchiveExtractCallback,
  public ICryptoGetTextPassword, public IArchiveRequestMemoryUseCallback, public CMyUnknownImp {
  Z7_COM_QI_BEGIN2(IFolderArchiveExtractCallback)
  Z7_COM_QI_ENTRY(ICryptoGetTextPassword)
  Z7_COM_QI_ENTRY(IArchiveRequestMemoryUseCallback)
  Z7_COM_QI_END
  Z7_COM_ADDREF_RELEASE
  Job &job;
  UString currentPath;
public:
  explicit ExtractCallback(Job &value) : job(value) {}
  Z7_COM7F_IMF(RequestMemoryUse(UInt32 f, UInt32 t, UInt32 i, const wchar_t *p, UInt64 r, UInt64 *a, UInt32 *v)) { return job.memory(f, t, i, p, r, a, v); }
  Z7_COM7F_IMF(SetTotal(UInt64 total)) { job.total = total; return job.check(); }
  Z7_COM7F_IMF(SetCompleted(const UInt64 *value)) { return job.progress(value); }
  Z7_COM7F_IMF(CryptoGetTextPassword(BSTR *password)) { return job.password(password); }
  Z7_COM7F_IMF(AskOverwrite(const wchar_t *existName, const FILETIME *existTime, const UInt64 *existSize,
    const wchar_t *newName, const FILETIME *newTime, const UInt64 *newSize, Int32 *answer)) {
    AvEvent e{}; e.kind = 4; e.path1 = existName; e.path2 = newName;
    if (existSize) { e.size1 = *existSize; e.flags |= 1; }
    if (newSize) { e.size2 = *newSize; e.flags |= 2; }
    if (existTime) { e.time1 = (UInt64(existTime->dwHighDateTime) << 32) | existTime->dwLowDateTime; e.flags |= 4; }
    if (newTime) { e.time2 = (UInt64(newTime->dwHighDateTime) << 32) | newTime->dwLowDateTime; e.flags |= 8; }
    int response = 5;
    RINOK(job.emit(e, nullptr, 0, &response))
    if (response < 0 || response > 5) return E_INVALIDARG;
    *answer = response;
    return S_OK;
  }
  Z7_COM7F_IMF(PrepareOperation(const wchar_t *name, Int32, Int32, const UInt64 *)) {
    RINOK(job.check())
    currentPath = name;
    AvEvent e{}; e.kind = 6; e.path1 = name; return job.emit(e);
  }
  Z7_COM7F_IMF(MessageError(const wchar_t *message)) {
    AvEvent e{}; e.kind = 5; e.path1 = message; return job.emit(e);
  }
  Z7_COM7F_IMF(SetOperationResult(Int32 result, Int32 encrypted)) {
    if (result == NArchive::NExtract::NOperationResult::kOK) return job.check();
    AvEvent e{}; e.kind = 5; e.code = (uint32_t)result; e.flags = (uint32_t)encrypted | 0x10000u; e.path1 = currentPath;
    return job.emit(e);
  }
};

uint64_t Number(IInArchive *arc, UInt32 index, PROPID id, bool *defined = nullptr) {
  NWindows::NCOM::CPropVariant p;
  if (arc->GetProperty(index, id, &p) != S_OK) return 0;
  if (defined) *defined = p.vt == VT_UI8 || p.vt == VT_UI4;
  return p.vt == VT_UI8 ? p.uhVal.QuadPart : p.vt == VT_UI4 ? p.ulVal : 0;
}

// Deliberately a regular byte stream: link/ADS semantics and archive-controlled
// paths must not escape into a temporary file used by an external application.
HRESULT CheckFile(Session &s, UInt32 index) {
  UInt32 count = 0; RINOK(s.link.GetArchive()->GetNumberOfItems(&count))
  if (index >= count) return E_INVALIDARG;
  bool value = false;
  RINOK(Archive_IsItem_Dir(s.link.GetArchive(), index, value))
  if (value) return E_INVALIDARG;
  RINOK(Archive_IsItem_AltStream(s.link.GetArchive(), index, value))
  if (value) return E_NOTIMPL;
  for (PROPID id : { kpidSymLink, kpidHardLink }) {
    NWindows::NCOM::CPropVariant p;
    RINOK(s.link.GetArchive()->GetProperty(index, id, &p))
    if (p.vt == VT_BSTR && p.bstrVal && *p.bstrVal) return E_NOTIMPL;
  }
  return S_OK;
}

class ExportCallback final : public IArchiveExtractCallback,
  public ICryptoGetTextPassword, public IArchiveRequestMemoryUseCallback, public CMyUnknownImp {
  Z7_COM_QI_BEGIN2(IArchiveExtractCallback)
  Z7_COM_QI_ENTRY(ICryptoGetTextPassword)
  Z7_COM_QI_ENTRY(IArchiveRequestMemoryUseCallback)
  Z7_COM_QI_END
  Z7_COM_ADDREF_RELEASE
  Job &job;
  UInt32 index;
  COutFileStream *file;
  CMyComPtr<ISequentialOutStream> stream;
public:
  ExportCallback(Job &j, UInt32 i, COutFileStream *f) : job(j), index(i), file(f), stream(f) {}
  Z7_COM7F_IMF(RequestMemoryUse(UInt32 f, UInt32 t, UInt32 i, const wchar_t *p, UInt64 r, UInt64 *a, UInt32 *v)) { return job.memory(f, t, i, p, r, a, v); }
  bool GotStream = false;
  bool Finished = false;
  Z7_COM7F_IMF(SetTotal(UInt64 n)) { job.total = n; return job.check(); }
  Z7_COM7F_IMF(SetCompleted(const UInt64 *n)) { return job.progress(n); }
  Z7_COM7F_IMF(CryptoGetTextPassword(BSTR *p)) { return job.password(p); }
  Z7_COM7F_IMF(GetStream(UInt32 i, ISequentialOutStream **out, Int32 mode)) {
    *out = nullptr; RINOK(job.check())
    if (i == index && mode == NArchive::NExtract::NAskMode::kExtract) {
      *out = stream; (*out)->AddRef(); GotStream = true;
    }
    return S_OK;
  }
  Z7_COM7F_IMF(PrepareOperation(Int32)) { return job.check(); }
  Z7_COM7F_IMF(SetOperationResult(Int32 result)) {
    if (result != NArchive::NExtract::NOperationResult::kOK) {
      AvEvent e{}; e.kind = 5; e.code = result; e.index = index;
      RINOK(job.emit(e))
      return E_FAIL;
    }
    Finished = true;
    return job.check();
  }
  HRESULT Close() { return file->Close(); }
};

class UpdateCallback final : public IFolderArchiveUpdateCallback,
  public IFolderArchiveUpdateCallback2, public ICryptoGetTextPassword,
  public ICryptoGetTextPassword2, public CMyUnknownImp {
  Z7_COM_QI_BEGIN2(IFolderArchiveUpdateCallback)
  Z7_COM_QI_ENTRY(IFolderArchiveUpdateCallback2)
  Z7_COM_QI_ENTRY(ICryptoGetTextPassword)
  Z7_COM_QI_ENTRY(ICryptoGetTextPassword2)
  Z7_COM_QI_END
  Z7_COM_ADDREF_RELEASE
  Job &job;
  bool encrypted;
  HRESULT path(const wchar_t *p) { AvEvent e{}; e.kind = 6; e.path1 = p; return job.emit(e); }
  HRESULT issue(const wchar_t *p, HRESULT code) {
    AvEvent e{}; e.kind = 5; e.path1 = p; e.code = (uint32_t)code;
    RINOK(job.emit(e))
    return FAILED(code) ? code : E_FAIL;
  }
public:
  UpdateCallback(Job &j, bool encrypt) : job(j), encrypted(encrypt) {}
  Z7_COM7F_IMF(SetTotal(UInt64 n)) { job.total = n; return job.check(); }
  Z7_COM7F_IMF(SetCompleted(const UInt64 *n)) { return job.progress(n); }
  Z7_COM7F_IMF(CryptoGetTextPassword(BSTR *p)) { return job.password(p); }
  Z7_COM7F_IMF(CryptoGetTextPassword2(Int32 *defined, BSTR *p)) {
    // Preserve encryption: a codec request always requires an explicit secret.
    *defined = 0; *p = nullptr;
    if (!encrypted) return job.check();
    RINOK(job.password(p))
    *defined = 1; return S_OK;
  }
  Z7_COM7F_IMF(CompressOperation(const wchar_t *p)) { return path(p); }
  Z7_COM7F_IMF(DeleteOperation(const wchar_t *p)) { return path(p); }
  Z7_COM7F_IMF(OperationResult(Int32 r)) { return r == 0 ? job.check() : issue(nullptr, E_FAIL); }
  Z7_COM7F_IMF(UpdateErrorMessage(const wchar_t *p)) { return issue(p, E_FAIL); }
  Z7_COM7F_IMF(SetNumFiles(UInt64)) { return job.check(); }
  Z7_COM7F_IMF(OpenFileError(const wchar_t *p, HRESULT h)) { return issue(p, h); }
  Z7_COM7F_IMF(ReadingFileError(const wchar_t *p, HRESULT h)) { return issue(p, h); }
  Z7_COM7F_IMF(ReportExtractResult(Int32 r, Int32, const wchar_t *p)) { return r == 0 ? job.check() : issue(p, E_FAIL); }
  Z7_COM7F_IMF(ReportUpdateOperation(UInt32, const wchar_t *p, Int32)) { RINOK(job.check()) return path(p); }
};

HRESULT BindFlatFolder(Session &session, CMyComPtr<IFolderFolder> &folder, CAgentFolder *&flat) {
  session.agent->_progress_for_Open = nullptr;
  RINOK(session.agentOwner->BindToRootFolder(&folder))
  CMyComPtr<IArchiveFolderInternal> internal;
  RINOK(folder.QueryInterface(IID_IArchiveFolderInternal, &internal))
  RINOK(internal->GetAgentFolder(&flat))
  RINOK(static_cast<IFolderSetFlatMode *>(flat)->SetFlatMode(1))
  return folder->LoadItems();
}
}

uint32_t av_abi() { return 5; }
void *av_job_create(AvCallback callback, void *context) {
  try { return new Job{callback, context}; } catch (...) { return nullptr; }
}
void av_job_cancel(void *value) {
  auto &j = *static_cast<Job *>(value);
  { std::lock_guard<std::mutex> lock(j.mutex); j.cancelled = true; }
  j.wake.notify_all();
}
int32_t av_job_check(void *value) {
  try { return value ? static_cast<Job *>(value)->check() : E_INVALIDARG; }
  catch (...) { return E_FAIL; }
}
void av_job_pause(void *value, int pause) {
  auto &j = *static_cast<Job *>(value);
  { std::lock_guard<std::mutex> lock(j.mutex); j.paused = pause != 0; }
  j.wake.notify_all();
}
void av_job_close(void *value) { delete static_cast<Job *>(value); }
void av_job_memory_limit(void *value, uint64_t bytes) { static_cast<Job *>(value)->memoryLimit = bytes; }
void av_close(void *value) { delete static_cast<Session *>(value); }

static int32_t OpenService(const wchar_t *path, void *value, void **result, const Byte *bytes = nullptr, size_t size = 0) {
  if (!path || !value || !result) return E_INVALIDARG;
  *result = nullptr;
  try {
    RINOK(static_cast<Job *>(value)->check())
    auto session = std::make_unique<Session>();
    // Load only our adjacent codec DLL, never registry-installed 7-Zip or plugins.
    RINOK(LoadAdjacentCodecs(*session->codecs))
    COpenOptions options;
    CObjectVector<COpenType> types;
    CIntVector excluded;
    options.types = &types;
    options.excludedFormats = &excluded;
    options.codecs = session->codecs;
    options.filePath = path;
    if (bytes) {
      auto *stream = new CBufferInStream;
      session->memoryStream = stream;
      stream->Buf.CopyFrom(bytes, size); stream->Init();
      options.stream = stream; session->virtualInput = true;
    }
    options.props = nullptr;
    OpenCallback callback(*static_cast<Job *>(value));
    RINOK(session->link.Open_Strict(options, &callback))
    session->agent->_archiveFilePath = path;
    for (unsigned i = 0; i < session->link.Arcs.Size(); ++i) {
      const auto &info = session->link.Arcs[i].ErrorInfo;
      if (info.ErrorFlags || info.WarningFlags) {
        AvEvent e{}; e.kind = 5; e.code = 0x100;
        e.size1 = info.ErrorFlags; e.size2 = info.WarningFlags;
        RINOK(static_cast<Job *>(value)->emit(e))
      }
    }
    *result = session.release();
    return S_OK;
  } catch (const std::bad_alloc &) { return E_OUTOFMEMORY; }
    catch (...) { return E_FAIL; }
}

int32_t av_open(const wchar_t *path, void *value, void **result) { return OpenService(path, value, result); }
int32_t av_open_memory(const uint8_t *data, uint64_t size, const wchar_t *name, void *job, void **result) {
  if (!data || !size || size > (64u << 20)) { if (result) *result = nullptr; return E_INVALIDARG; }
  return OpenService(name, job, result, data, (size_t)size);
}

int32_t av_write_formats(void *value) {
  if (!value) return E_INVALIDARG;
  try {
    auto &job = *static_cast<Job *>(value);
    Session services;
    RINOK(LoadAdjacentCodecs(*services.codecs))
    for (unsigned i = 0; i < services.codecs->Formats.Size(); ++i) {
      const auto &format = services.codecs->Formats[i];
      if (!format.UpdateEnabled || format.Flags_HashHandler()) continue;
      const UString ext = format.GetMainExt();
      AvEvent e{}; e.kind = 11; e.path1 = format.Name; e.path2 = ext;
      e.flags = (format.Flags_KeepName() ? 1u : 0u) | (format.Flags_SymLinks() ? 2u : 0u)
        | (format.Flags_HardLinks() ? 4u : 0u) | (format.Flags_AltStreams() ? 8u : 0u)
        | (format.Flags_NtSecurity() ? 16u : 0u) | (format.Flags_CTime() ? 32u : 0u)
        | (format.Flags_MTime() ? 64u : 0u) | (format.Flags_ATime() ? 128u : 0u);
      e.size1 = format.Get_TimePrecFlags();
      e.size2 = format.Get_DefaultTimePrec();
      RINOK(job.emit(e))
    }
    return job.check();
  } catch (const std::bad_alloc &) { return E_OUTOFMEMORY; } catch (...) { return E_FAIL; }
}

int32_t av_write_memory(const wchar_t *format, const wchar_t *method, uint32_t level,
    uint32_t threads, uint64_t dictionary, uint64_t xzBlock, uint64_t limit, uint32_t percent,
    uint64_t *compression, uint64_t *decompression, uint64_t *budget) {
  if (!format || !method || !compression || !decompression || !budget || level > 9 ||
      dictionary > ((UInt64)1 << 32) || xzBlock > ((UInt64)1 << 48)) return E_INVALIDARG;
  try {
    const UInt64 MiB = 1 << 20;
    const bool sevenZip = StringsAreEqualNoCase_Ascii(format, "7z");
    const bool zip = StringsAreEqualNoCase_Ascii(format, "zip");
    const bool xz = StringsAreEqualNoCase_Ascii(format, "xz");
    const bool lzma = StringsAreEqualNoCase_Ascii(method, "LZMA");
    const bool lzma2 = StringsAreEqualNoCase_Ascii(method, "LZMA2");
    const bool ppmd = StringsAreEqualNoCase_Ascii(method, "PPMd");
    const bool bzip2 = StringsAreEqualNoCase_Ascii(method, "BZip2");
    const bool deflate = StringsAreEqualNoCase_Ascii(method, "Deflate") ||
        StringsAreEqualNoCase_Ascii(method, "Deflate64");
    size_t ram = sizeof(size_t) << 29;
    NWindows::NSystem::GetRamSize(ram);
    const UInt64 reducedRam = std::max<UInt64>(64 * MiB, ram);
    *budget = limit != 0 ? limit : Calc_From_Val_Percents(reducedRam, percent ? percent : 80);
    if ((level == 0 && (sevenZip || zip)) || StringsAreEqualNoCase_Ascii(method, "Copy")) {
      *compression = *decompression = MiB;
      return S_OK;
    }
    if (!(lzma || lzma2 || ppmd || bzip2 || deflate)) return S_FALSE;

    const UInt32 maxThreads = zip ? 128 : lzma2 ? 512 : bzip2 ? 64 : lzma ? 2 : 1;
    const UInt32 requested = threads ? threads : GetActiveProcessorCount(ALL_PROCESSOR_GROUPS);
    UInt32 workers = std::max<UInt32>(1, std::min<UInt32>(requested, maxThreads));
    CMethodProps props;
    props.AddProp_Level(level);
    if (dictionary != 0) {
      auto &p = props.Props.AddNew();
      p.Id = ppmd ? NCoderPropID::kUsedMemorySize : NCoderPropID::kDictionarySize;
      p.Value = (UInt64)dictionary;
    }
    if (xz && xzBlock > 1) props.AddProp_BlockSize2(xzBlock);

    // Use the encoder's defaults and match-finder estimates. Add the GUI's
    // archive-level ZIP buffers, LZMA2 blocks and Ultra filter overhead.
    auto estimate = [&](UInt32 n) {
      const UInt32 perCoder = std::min<UInt32>(n, (lzma || lzma2) && level >= 5 ? 2u : 1u);
      UInt32 blocks = n / perCoder;
      UInt64 size = sevenZip && level >= 9 ? 29 * MiB : 0;
      if (zip && blocks > 1) size += (UInt64)blocks * (sizeof(size_t) << 23);
      if (lzma || lzma2) {
        CMethodProps coder = props;
        coder.AddProp_NumThreads(perCoder);
        *decompression = std::min<UInt64>(coder.Get_Lzma_DicSize(), (UInt64)15 << 28) + 2 * MiB;
        if (xzBlock == 1 || (!lzma2 && !zip)) blocks = 1;
        if (lzma2 && blocks > 1) {
          const UInt64 chunk = coder.Get_Xz_BlockSize();
          return size + blocks * coder.Get_Lzma_MemUsage(false) + (blocks * 2 + blocks / 8 + 1) * chunk;
        }
        return size + blocks * coder.Get_Lzma_MemUsage(true);
      }
      if (ppmd) {
        *decompression = props.Get_Ppmd_MemSize() + 2 * MiB;
        return size + *decompression * (zip ? n : 1u);
      }
      if (bzip2) {
        *decompression = 7 * MiB;
        return size + 10 * MiB * n;
      }
      *decompression = 2 * MiB;
      return size + 4 * MiB * blocks;
    };
    *compression = estimate(workers);
    if (!threads && (zip || lzma2)) {
      const UInt32 step = lzma2 && level >= 5 ? 2 : 1;
      while (*compression > *budget && workers > step) {
        workers -= step;
        *compression = estimate(workers);
      }
    }
    return S_OK;
  } catch (const std::bad_alloc &) { return E_OUTOFMEMORY; } catch (...) { return E_FAIL; }
}

int32_t av_write_archive(void *value, const AvWriteOptions *request,
  const wchar_t * const *paths, uint32_t count, const wchar_t * const *propertyNames,
  const wchar_t * const *propertyValues, const uint64_t *volumeSizes) {
  if (!value || !request || !request->format || !request->output || !*request->output ||
      !paths || !count || request->action > 3 || request->pathMode > 2 ||
      (request->numProperties && (!propertyNames || !propertyValues)) ||
      (request->numVolumes && (!volumeSizes || request->source))) return E_INVALIDARG;
  auto &job = *static_cast<Job *>(value);
  try {
    RINOK(job.check())
    Session services;
    RINOK(LoadAdjacentCodecs(*services.codecs))
    const int index = services.codecs->FindFormatForArchiveType(request->format);
    if (index < 0) return E_NOTIMPL;
    const auto &format = services.codecs->Formats[(unsigned)index];
    if (!format.UpdateEnabled || format.Flags_HashHandler()) return E_NOTIMPL;
    if (request->sfxModule && !format.Is_7z()) return E_INVALIDARG;
    if (format.Flags_KeepName()) {
      if (count != 1 || !paths[0]) return E_INVALIDARG;
      NWindows::NFile::NFind::CFileInfo info;
      if (!info.Find_FollowLink(paths[0]) || info.IsDir()) return E_INVALIDARG;
    }
    CObjectVector<COpenType> types;
    COpenType type;
    type.FormatIndex = index;
    types.Add(type);
    CUpdateOptions options;
    options.UpdateArchiveItself = false;
    options.ArcNameMode = k_ArcNameMode_Exact;
    options.PathMode = static_cast<NWildcard::ECensorPathMode>(request->pathMode);
    options.OpenShareForWrite = (request->flags & 1) != 0;
    options.SymLinks.Val = (request->flags & 2) != 0;
    options.HardLinks.Val = (request->flags & 4) != 0;
    options.AltStreams.Val = (request->flags & 8) != 0;
    options.NtSecurity.Val = (request->flags & 16) != 0;
    options.SetArcMTime = (request->flags & 32) != 0;
    options.DeleteAfterCompressing = (request->flags & 64) != 0;
    if (options.DeleteAfterCompressing && (request->action != 0 ||
        options.OpenShareForWrite)) return E_INVALIDARG;
    if (request->sfxModule) {
      options.SfxMode = true;
      options.SfxModule = request->sfxModule;
    }
    const NUpdateArchive::CActionSet *actions[] = { &NUpdateArchive::k_ActionSet_Add,
      &NUpdateArchive::k_ActionSet_Update, &NUpdateArchive::k_ActionSet_Fresh, &NUpdateArchive::k_ActionSet_Sync };
    CUpdateArchiveCommand command;
    command.UserArchivePath = request->output;
    command.ActionSet = *actions[request->action];
    options.Commands.Add(command);
    for (uint32_t i = 0; i < request->numProperties; ++i) {
      if (!propertyNames[i] || !*propertyNames[i] || !propertyValues[i]) return E_INVALIDARG;
      CProperty property;
      property.Name = propertyNames[i];
      property.Value = propertyValues[i];
      options.MethodMode.Properties.Add(property);
    }
    for (uint32_t i = 0; i < request->numVolumes; ++i) {
      if (!volumeSizes[i]) return E_INVALIDARG;
      options.VolumesSizes.Add(volumeSizes[i]);
    }
    NWildcard::CCensor censor;
    UStringVector removablePaths;
    CBoolVector removableDirectories;
    for (uint32_t i = 0; i < count; ++i) {
      if (!paths[i] || !*paths[i]) return E_INVALIDARG;
      if (options.DeleteAfterCompressing) {
        NWindows::NFile::NFind::CFileInfo info;
        if (!info.Find(paths[i], false) || (info.Attrib & FILE_ATTRIBUTE_REPARSE_POINT))
          return E_INVALIDARG;
        removablePaths.Add(paths[i]);
        removableDirectories.Add(info.IsDir());
      }
      censor.AddPreItem_NoWildcard(paths[i]);
    }
    WriteCallback callback(job, request->password, removablePaths, removableDirectories);
    CUpdateErrorInfo error;
    const HRESULT h = UpdateArchive(services.codecs, types, request->source ? request->source : L"",
      censor, options, error, &callback, &callback, true);
    if (h != S_OK && h != E_ABORT && error.ThereIsError()) {
      UString message;
      message = error.Message;
      for (unsigned i = 0; i < error.FileNames.Size(); ++i) {
        if (!message.IsEmpty()) message.Add_LF();
        message += error.FileNames[i];
      }
      AvEvent e{}; e.kind = 5; e.path1 = message;
      RINOK(job.emit(e))
    }
    return h;
  } catch (const std::bad_alloc &) { return E_OUTOFMEMORY; }
    catch (const wchar_t *message) { AvEvent e{}; e.kind = 5; e.path1 = message; job.emit(e); return E_FAIL; }
    catch (const char *message) { UString s; s = message; AvEvent e{}; e.kind = 5; e.path1 = s; job.emit(e); return E_FAIL; }
    catch (...) { return E_FAIL; }
}

int32_t av_list(void *sessionValue, void *jobValue) {
  if (!sessionValue || !jobValue) return E_INVALIDARG;
  try {
    auto &session = *static_cast<Session *>(sessionValue);
    auto &job = *static_cast<Job *>(jobValue);
    IInArchive *arc = session.link.GetArchive();
    UInt32 count = 0; RINOK(arc->GetNumberOfItems(&count))
    for (UInt32 i = 0; i < count; ++i) {
      RINOK(job.check())
      UString path; RINOK(session.link.GetArc()->GetItem_Path(i, path))
      bool dir = false; RINOK(Archive_IsItem_Dir(arc, i, dir))
      AvEvent e{}; e.kind = 1; e.index = i; e.path1 = path;
      e.flags = dir ? 1u : 0u;
      bool hasSize = false;
      e.size1 = Number(arc, i, kpidSize, &hasSize);
      if (hasSize) e.flags |= 2;
      e.size2 = Number(arc, i, kpidPackSize);
      NWindows::NCOM::CPropVariant time;
      RINOK(arc->GetProperty(i, kpidMTime, &time))
      if (time.vt == VT_FILETIME) {
        e.time1 = (UInt64(time.filetime.dwHighDateTime) << 32) | time.filetime.dwLowDateTime;
        e.flags |= 4;
      }
      RINOK(job.emit(e))
    }
    return S_OK;
  } catch (const std::bad_alloc &) { return E_OUTOFMEMORY; } catch (...) { return E_FAIL; }
}

int32_t av_columns(void *sessionValue, void *jobValue) {
  if (!sessionValue || !jobValue) return E_INVALIDARG;
  try {
    auto *arc = static_cast<Session *>(sessionValue)->link.GetArchive();
    auto &job = *static_cast<Job *>(jobValue);
    UInt32 count = 0; RINOK(arc->GetNumberOfProperties(&count))
    for (UInt32 i = 0; i < count; ++i) {
      RINOK(job.check())
      CMyComBSTR name; PROPID id; VARTYPE type;
      RINOK(arc->GetPropertyInfo(i, &name, &id, &type))
      AvEvent e{}; e.kind = 9; e.code = id; e.flags = type; e.path1 = name;
      RINOK(job.emit(e))
    }
    return S_OK;
  } catch (...) { return E_FAIL; }
}
int32_t av_property(void *sessionValue, void *jobValue, uint32_t property) {
  if (!sessionValue || !jobValue) return E_INVALIDARG;
  try {
    auto *arc = static_cast<Session *>(sessionValue)->link.GetArchive();
    auto &job = *static_cast<Job *>(jobValue);
    UInt32 count = 0; RINOK(arc->GetNumberOfItems(&count))
    for (UInt32 i = 0; i < count; ++i) {
      RINOK(job.check())
      NWindows::NCOM::CPropVariant value; RINOK(arc->GetProperty(i, property, &value))
      UString text; ConvertPropertyToString2(text, value, property);
      AvEvent e{}; e.kind = 10; e.code = property; e.index = i; e.flags = value.vt; e.path1 = text;
      switch (value.vt) {
        case VT_UI1: e.size1 = value.bVal; break;
        case VT_UI2: e.size1 = value.uiVal; break;
        case VT_UI4: e.size1 = value.ulVal; break;
        case VT_UI8: e.size1 = value.uhVal.QuadPart; break;
        case VT_I1: e.size1 = (UInt64)(Int64)value.cVal; break;
        case VT_I2: e.size1 = (UInt64)(Int64)value.iVal; break;
        case VT_I4: e.size1 = (UInt64)(Int64)value.lVal; break;
        case VT_I8: e.size1 = (UInt64)value.hVal.QuadPart; break;
        case VT_BOOL: e.size1 = value.boolVal != VARIANT_FALSE; break;
        case VT_FILETIME: e.size1 = (UInt64(value.filetime.dwHighDateTime) << 32) | value.filetime.dwLowDateTime; break;
        case VT_R4: memcpy(&e.size1, &value.fltVal, sizeof(value.fltVal)); break;
        case VT_R8: memcpy(&e.size1, &value.dblVal, sizeof(value.dblVal)); break;
        default: break;
      }
      RINOK(job.emit(e))
    }
    return S_OK;
  } catch (...) { return E_FAIL; }
}

int32_t av_extract(void *sessionValue, void *jobValue, const uint32_t *indices,
  uint32_t count, const wchar_t *destination, int test, uint32_t pathMode,
  uint32_t overwriteMode, uint32_t flags, const wchar_t *relativePrefix) {
  if (!sessionValue || !jobValue || !indices || !count || (!test && (!destination || !*destination)) ||
      pathMode > NExtract::NPathMode::kAbsPaths || overwriteMode > NExtract::NOverwriteMode::kRenameExisting) return E_INVALIDARG;
  try {
    auto &session = *static_cast<Session *>(sessionValue);
    auto &job = *static_cast<Job *>(jobValue);
    RINOK(job.check())
    UInt32 itemCount = 0; RINOK(session.link.GetArchive()->GetNumberOfItems(&itemCount))
    CUIntVector selected;
    for (uint32_t i = 0; i < count; ++i) {
      if (indices[i] >= itemCount || (i && indices[i] <= indices[i - 1])) return E_INVALIDARG;
      if (test && (flags & 4)) {
        NWindows::NCOM::CPropVariant encrypted;
        RINOK(session.link.GetArchive()->GetProperty(indices[i], kpidEncrypted, &encrypted))
        if (encrypted.vt != VT_BOOL || encrypted.boolVal == VARIANT_FALSE) continue;
      }
      selected.Add(indices[i]);
    }
    if (selected.IsEmpty()) return S_OK;
    FString dest;
    if (!test) {
      dest = destination;
      NWindows::NFile::NName::NormalizeDirPathPrefix(dest);
      if (!NWindows::NFile::NDir::CreateComplexDir(dest)) return HRESULT_FROM_WIN32(GetLastError());
    }
    auto *uiSpec = new ExtractCallback(job);
    CMyComPtr<IFolderArchiveExtractCallback> ui = uiSpec;
    auto *ecs = new CArchiveExtractCallback;
    CMyComPtr<IArchiveExtractCallback> callback = ecs;
    ecs->InitForMulti(false, static_cast<NExtract::NPathMode::EEnum>(pathMode),
      static_cast<NExtract::NOverwriteMode::EEnum>(overwriteMode),
      NExtract::NZoneIdMode::kNone, true);
    ecs->InitBeforeNewArchive();
    CExtractNtOptions options;
    options.NtSecurity.Val = (flags & 1) != 0;
    options.AltStreams.Val = (flags & 2) != 0;
    UStringVector removeParts;
    if (pathMode == NExtract::NPathMode::kCurPaths && relativePrefix && *relativePrefix)
      SplitPathToParts(UString(relativePrefix), removeParts);
    ecs->Init(options, nullptr, session.link.GetArc(), ui, false, test != 0, dest,
      removeParts, false, (UInt64)(Int64)-1);
    CArchiveExtractCallback_Closer closer(ecs);
    if (!test) { RINOK(ecs->PrepareHardLinks(&selected)) }
    HRESULT h = session.link.GetArchive()->Extract(selected.ConstData(), selected.Size(), test, callback);
    HRESULT closeResult = closer.Close();
    return h == S_OK ? closeResult : h;
  } catch (const std::bad_alloc &) { return E_OUTOFMEMORY; } catch (...) { return E_FAIL; }
}

int32_t av_export_item(void *sessionValue, void *jobValue, uint32_t index, const wchar_t *output) {
  if (!sessionValue || !jobValue || !output || !*output) return E_INVALIDARG;
  try {
    auto &session = *static_cast<Session *>(sessionValue);
    auto &job = *static_cast<Job *>(jobValue);
    RINOK(job.check())
    RINOK(CheckFile(session, index))
    auto *file = new COutFileStream;
    CMyComPtr<IOutStream> owner = file;
    if (!file->Create_NEW(output)) return HRESULT_FROM_WIN32(GetLastError());
    auto *spec = new ExportCallback(job, index, file);
    CMyComPtr<IArchiveExtractCallback> callback = spec;
    HRESULT h = session.link.GetArchive()->Extract(&index, 1, false, callback);
    HRESULT closed = spec->Close();
    if (h != S_OK) return h;
    if (closed != S_OK) return closed;
    return spec->GotStream && spec->Finished ? S_OK : E_FAIL;
  } catch (const std::bad_alloc &) { return E_OUTOFMEMORY; }
    catch (...) { return E_FAIL; }
}

int32_t av_fs_transfer(void *value, const wchar_t * const *paths, uint32_t count,
  const wchar_t *destination, int move) {
  if (!value || !paths || !count || !destination || !*destination) return E_INVALIDARG;
  try {
    auto &job = *static_cast<Job *>(value); RINOK(job.check())
    UStringVector sources;
    for (uint32_t i = 0; i < count; ++i) {
      if (!paths[i] || !*paths[i]) return E_INVALIDARG;
      sources.Add(paths[i]);
    }
    FString target = destination;
    NWindows::NFile::NName::NormalizeDirPathPrefix(target);
    auto *spec = new FileCopyCallback(job);
    CMyComPtr<IFolderOperationsExtractCallback> callback = spec;
    return NFsFolder::CopyFileSystemItems(sources, target, move != 0, callback);
  } catch (const std::bad_alloc &) { return E_OUTOFMEMORY; } catch (...) { return E_FAIL; }
}

int32_t av_fs_delete(void *value, const wchar_t * const *paths, uint32_t count,
  uintptr_t owner, int recycle) {
  if (!value || !paths || !count) return E_INVALIDARG;
  try {
    auto &job = *static_cast<Job *>(value); RINOK(job.check())
    const HRESULT initialized = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (FAILED(initialized)) return initialized;
    struct Uninit { ~Uninit() { CoUninitialize(); } } uninit;
    CMyComPtr<IFileOperation> operation;
    RINOK(CoCreateInstance(__uuidof(FileOperation), nullptr, CLSCTX_INPROC_SERVER,
      __uuidof(IFileOperation), (void **)&operation))
    DWORD flags = FOF_NO_CONNECTED_ELEMENTS;
    if (recycle) flags |= FOFX_RECYCLEONDELETE | FOF_WANTNUKEWARNING;
    // The caller has already confirmed the exact selection. Fixture operations
    // have no window; fail rather than displaying an unattended error dialog.
    if (!owner) flags |= FOF_SILENT | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOFX_EARLYFAILURE;
    RINOK(operation->SetOperationFlags(flags))
    RINOK(operation->SetOwnerWindow(reinterpret_cast<HWND>(owner)))
    auto *spec = new DeleteSink(job);
    CMyComPtr<IFileOperationProgressSink> sink = spec;
    DWORD cookie = 0; RINOK(operation->Advise(sink, &cookie))
    struct Unadvise { IFileOperation *op; DWORD cookie; ~Unadvise() { op->Unadvise(cookie); } } connection{operation, cookie};
    for (uint32_t i = 0; i < count; ++i) {
      RINOK(job.check())
      if (!paths[i] || !*paths[i]) return E_INVALIDARG;
      CMyComPtr<IShellItem> item;
      RINOK(SHCreateItemFromParsingName(paths[i], nullptr, __uuidof(IShellItem), (void **)&item))
      RINOK(operation->DeleteItem(item, nullptr))
    }
    const HRESULT h = operation->PerformOperations();
    BOOL aborted = FALSE;
    RINOK(operation->GetAnyOperationsAborted(&aborted))
    if (job.cancelled || h == COPYENGINE_E_USER_CANCELLED || spec->failure == COPYENGINE_E_USER_CANCELLED ||
        h == HRESULT_FROM_WIN32(ERROR_CANCELLED) || spec->failure == HRESULT_FROM_WIN32(ERROR_CANCELLED)) return E_ABORT;
    if (FAILED(h)) return h;
    if (FAILED(spec->failure)) return spec->failure;
    return aborted ? E_ABORT : S_OK;
  } catch (const std::bad_alloc &) { return E_OUTOFMEMORY; } catch (...) { return E_FAIL; }
}

// Preserve the carrier prefix as CommonUpdateOperation does. The archive
// writer's seek/size origin starts after that prefix.
static HRESULT GetUpdateOutput(const CArc &arc, IOutStream *stream, CMyComPtr<IOutStream> &tail) {
  if (arc.ArcStreamOffset == 0) {
    tail = stream;
    return S_OK;
  }
  if (arc.Offset < 0) return E_NOTIMPL;
  RINOK(arc.InStream->Seek(0, STREAM_SEEK_SET, nullptr))
  RINOK(NCompress::CopyStream_ExactSize(arc.InStream, stream, arc.ArcStreamOffset, nullptr))
  auto *offset = new CTailOutStream;
  tail = offset;
  offset->Stream = stream;
  offset->Offset = arc.ArcStreamOffset;
  offset->Init();
  return S_OK;
}

int32_t av_replace_item(void *sessionValue, void *jobValue, uint32_t index,
  const wchar_t *replacement, const wchar_t *output) {
  if (!sessionValue || !jobValue || !replacement || !*replacement || !output || !*output) return E_INVALIDARG;
  try {
    auto &session = *static_cast<Session *>(sessionValue);
    if (session.virtualInput) return E_NOTIMPL;
    auto &job = *static_cast<Job *>(jobValue);
    RINOK(job.check())
    RINOK(CheckFile(session, index))
    auto &agent = *session.agent;
    if (!agent.CanUpdate() || agent.IsThere_ReadOnlyArc()) return E_NOTIMPL;
    CMyComPtr<IFolderFolder> folder;
    CAgentFolder *flat = nullptr;
    RINOK(BindFlatFolder(session, folder, flat))
    UInt32 n = 0; RINOK(folder->GetNumberOfItems(&n))
    UInt32 mapped = n;
    for (UInt32 i = 0; i < n; ++i) {
      RINOK(job.check())
      if (flat->GetRealIndex(i) == (int)index) { mapped = i; break; }
    }
    if (mapped == n) return E_INVALIDARG;
    RINOK(agent.SetFolder(folder))
    struct ResetFolder { CAgent &agent; ~ResetFolder() { agent.SetFolder(nullptr); } } reset{agent};
    // This does not call CommonUpdateOperation/MoveToOriginal: no registry work
    // directory preference or mutation of the input archive is involved.
    auto *file = new COutFileStream;
    CMyComPtr<IOutStream> stream = file;
    if (!file->Create_NEW(output)) return HRESULT_FROM_WIN32(GetLastError());
    CMyComPtr<IOutStream> tail;
    RINOK(GetUpdateOutput(agent.GetArc(), stream, tail))
    bool encrypted = false;
    RINOK(Archive_GetItemBoolProp(session.link.GetArchive(), index, kpidEncrypted, encrypted))
    auto *spec = new UpdateCallback(job, encrypted || session.link.PasswordWasAsked);
    CMyComPtr<IFolderArchiveUpdateCallback> callback = spec;
    HRESULT h = agent.UpdateOneFile(tail, &mapped, 1, replacement, callback);
    HRESULT closed = file->Close();
    return h == S_OK ? closed : h;
  } catch (const std::bad_alloc &) { return E_OUTOFMEMORY; }
    catch (...) { return E_FAIL; }
}

int32_t av_can_delete_items(void *sessionValue) {
  if (!sessionValue) return 0;
  const auto &session = *static_cast<Session *>(sessionValue);
  auto &agent = *session.agent;
  return !session.virtualInput && agent.CanUpdate() && !agent.IsThere_ReadOnlyArc() &&
    session.link.Arcs.Size() == 1 && session.link.VolumePaths.Size() <= 1 &&
    agent.GetArc().Offset >= 0 && agent.GetArc().ErrorInfo.ErrorFlags == 0 &&
    agent.GetArc().ErrorInfo.WarningFlags == 0;
}

int32_t av_delete_items(void *sessionValue, void *jobValue, const uint32_t *indices,
  uint32_t count, const wchar_t *output) {
  if (!sessionValue || !jobValue || !indices || !count || !output || !*output) return E_INVALIDARG;
  if (!av_can_delete_items(sessionValue)) return E_NOTIMPL;
  try {
    auto &session = *static_cast<Session *>(sessionValue);
    auto &job = *static_cast<Job *>(jobValue);
    auto &agent = *session.agent;
    RINOK(job.check())
    if (!std::is_sorted(indices, indices + count) ||
      std::adjacent_find(indices, indices + count) != indices + count) return E_INVALIDARG;
    CMyComPtr<IFolderFolder> folder;
    CAgentFolder *flat = nullptr;
    RINOK(BindFlatFolder(session, folder, flat))
    if (!agent.CanUpdate()) return E_NOTIMPL;
    UInt32 n = 0;
    RINOK(folder->GetNumberOfItems(&n))
    CUIntVector selected;
    for (UInt32 i = 0; i < n; ++i) {
      RINOK(job.check())
      int index = flat->GetRealIndex(i);
      if (index >= 0 && std::binary_search(indices, indices + count, static_cast<uint32_t>(index)))
        selected.Add(i);
    }
    if (selected.Size() != count) return E_INVALIDARG;
    RINOK(agent.SetFolder(folder))
    struct ResetFolder { CAgent &agent; ~ResetFolder() { agent.SetFolder(nullptr); } } reset{agent};
    auto *file = new COutFileStream;
    CMyComPtr<IOutStream> stream = file;
    if (!file->Create_NEW(output)) return HRESULT_FROM_WIN32(GetLastError());
    CMyComPtr<IOutStream> tail;
    RINOK(GetUpdateOutput(agent.GetArc(), stream, tail))
    auto *spec = new UpdateCallback(job, session.link.PasswordWasAsked);
    CMyComPtr<IFolderArchiveUpdateCallback> callback = spec;
    const HRESULT h = agent.DeleteItems(tail, selected.ConstData(), selected.Size(), callback);
    const HRESULT closed = file->Close();
    return h == S_OK ? closed : h;
  } catch (const std::bad_alloc &) { return E_OUTOFMEMORY; } catch (...) { return E_FAIL; }
}
