#pragma once
#include <stdint.h>
#include <wchar.h>
// ABI 5. All strings are UTF-16, borrowed only until the callback returns.
// Functions run synchronously on the caller's worker. A session is single-owner,
// non-reentrant; cancel/pause are the only concurrent calls. Never close in use.
struct AvEvent {
  uint32_t kind, code, index, flags;
  uint64_t completed, total, size1, size2, time1, time2;
  const wchar_t *path1, *path2;
};
// 1=item, 2=progress, 3=password, 4=overwrite, 5=issue, 6=current path,
// 7=pause acknowledged. Password: return 0 and fill reply; negative=cancel.
// Overwrite response: upstream NOverwriteAnswer (0=yes,1=all,2=no,3=no all,
// 4=auto-rename incoming,5=cancel). No exceptions may escape callback.
typedef int (__cdecl *AvCallback)(void *, const AvEvent *, wchar_t *reply, uint32_t capacity);
#define AV_API extern "C" __declspec(dllexport)
AV_API uint32_t av_abi();
AV_API void *av_job_create(AvCallback callback, void *context);
AV_API void av_job_cancel(void *job);
// Cooperative checkpoint for managed hashing/copy on the same operation worker.
AV_API int32_t av_job_check(void *job);
AV_API void av_job_pause(void *job, int pause);
// Set before work; UINT64_MAX retains the codec's default limit.
AV_API void av_job_memory_limit(void *job, uint64_t bytes);
AV_API void av_job_close(void *job);
AV_API int32_t av_open(const wchar_t *path, void *job, void **session);
// Bounded virtual input proof: copy at most 64 MiB into session-owned memory.
// Read/extract only; Agent update-back requires the file-backed session route.
AV_API int32_t av_open_memory(const uint8_t *data, uint64_t size, const wchar_t *name, void *job, void **session);
AV_API int32_t av_list(void *session, void *job);
// 9=column schema (code=property id, flags=VARTYPE, path1=optional name).
// 10=column value (code=property id, index=native index, path1=display text,
// flags=VARTYPE, size1=integer/filetime or IEEE floating bits for numeric sorting).
AV_API int32_t av_columns(void *session, void *job);
AV_API int32_t av_property(void *session, void *job, uint32_t property);
AV_API int32_t av_extract(void *session, void *job, const uint32_t *indices,
  uint32_t count, const wchar_t *destination, int test, uint32_t pathMode,
  uint32_t overwriteMode, uint32_t flags, const wchar_t *relativePrefix);
// Materialize exactly one native index into a new caller-owned regular file.
// No archive path is used as an output path. Neither call overwrites a file.
AV_API int32_t av_export_item(void *session, void *job, uint32_t index, const wchar_t *output);
// Reuse Agent's index-based UpdateOneFile; write a new archive, leaving the
// source/session intact. The caller owns commit/recovery of this staged output.
AV_API int32_t av_replace_item(void *session, void *job, uint32_t index,
  const wchar_t *replacement, const wchar_t *output);
AV_API int32_t av_can_delete_items(void *session);
AV_API int32_t av_delete_items(void *session, void *job, const uint32_t *indices,
  uint32_t count, const wchar_t *output);
AV_API void av_close(void *session);
// 11=write format (path1=name, path2=extension, flags=capabilities,
// size1=timestamp precision mask, size2=default precision).
// 12=publish staged outputs before deleting sources (reply 1 authorizes deletion).
AV_API int32_t av_write_formats(void *job);
// Zero inputs select encoder defaults. xzBlock=1 selects an unbroken stream.
// S_FALSE means no estimate is available for this method.
AV_API int32_t av_write_memory(const wchar_t *format, const wchar_t *method, uint32_t level,
  uint32_t threads, uint64_t dictionary, uint64_t xzBlock, uint64_t limit, uint32_t percent,
  uint64_t *compression, uint64_t *decompression, uint64_t *budget);
struct AvWriteOptions {
  const wchar_t *format, *source, *output, *password, *sfxModule;
  uint32_t numProperties, numVolumes, action, pathMode, flags;
};
// Writes only to an empty caller-owned staging location. An optional existing
// archive is read as input. The caller owns publishing all resulting files.
AV_API int32_t av_write_archive(void *job, const AvWriteOptions *options,
  const wchar_t * const *paths, uint32_t count, const wchar_t * const *propertyNames,
  const wchar_t * const *propertyValues, const uint64_t *volumeSizes);
// Filesystem inputs are explicit absolute paths, never wildcard expressions.
// Transfer reuses the upstream FS provider and existing overwrite events.
AV_API int32_t av_fs_transfer(void *job, const wchar_t * const *paths, uint32_t count,
  const wchar_t *destination, int move);
// Windows owns recycle semantics and any native safety/error dialogs. Owner is
// the main HWND; zero is reserved for noninteractive fixture checks.
AV_API int32_t av_fs_delete(void *job, const wchar_t * const *paths, uint32_t count,
  uintptr_t owner, int recycle);
