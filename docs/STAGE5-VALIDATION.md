# Stage 5 — archive workflows

2026-11-20. Archive workflows in the portable x64 development build. WinUI 3 uses
native bridge ABI 5.

- **Add** uses the pinned 7-Zip `UpdateArchive` service and loads writable formats
  from the adjacent `7z.dll`. The WinUI dialog exposes compression method and
  level, dictionary and order, threads, memory limit, solid blocks, encryption,
  volumes, SFX, metadata selection, timestamp precision, path modes and update
  modes. The dialog estimates compression and decompression memory from the
  selected method, level, dictionary, block size and thread count. It shows the
  memory budget and flags estimates above it. Raw codec overrides disable the
  estimate. Compression settings are remembered per format. The portable build
  includes the upstream `7z.sfx` module.
- **Update** writes a complete staged archive, holds a read lease on the input
  during encoding, then replaces the destination. It checks the prior archive
  retained by the atomic replacement. Conflicts or filesystem failures retain
  recovery files. Publication of a new archive or its volumes never overwrites
  an existing file. When creating or updating an archive in Add mode, the
  option to remove selected regular files and folders delegates cleanup to
  7-Zip after publication.
  Cleanup rejects reparse-point paths and reports inputs left on disk.
- **Extract and Test** work on an open archive or files selected in a filesystem
  panel. Extraction offers path and overwrite modes, NTFS metadata, a password,
  and separate folders per archive. Copy and F5 copy the selected members out of
  an open archive. Encrypted headers offer a password retry during open.
  Extract checks encrypted members before writing to the destination, retries
  rejected passwords and reuses the validated password for the write. This adds
  a decoding pass for those members.
- **Delete from archive** maps selected members by native index, stages and
  verifies the result, then reopens the archive. A successful replacement
  keeps a backup beside the archive. Nested mutations rebuild the parent chain.
  SFX updates preserve the executable prefix. Split archives, read-only
  handlers and archives with unresolved structural warnings are excluded.

The bridge checks verified 7z, ZIP, TAR, GZip, BZip2 and XZ with the pinned
original 7-Zip console. They compared member bytes, checked all four update
modes, restored an exact ZIP creation time, checked a preserved SFX stub and
reopened a nested archive after deletion. They also checked cancellation during
  an update, split-volume and replacement conflicts before source cleanup,
  folder cleanup and password retries for ZIP AES and 7z member data.

Windows 11 passed 157 bridge, 47 filesystem, 91 WinUI command and interaction,
and 34 WinUI navigation and layout checks. Desktop checks exercised the
format-specific Add controls, then created, extracted, copied and updated real
archive files through the WinUI commands.

Remaining parity work covers drag/drop and clipboard for archive members,
delete-after-compression when traversing links, and validation of unusual NTFS
metadata and less common codec options.
Physical Explorer interaction, Windows 10 and clean-machine installation remain
unverified. Explorer registration and release validation belong to stages 7–8.
