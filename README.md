# XbRecUnpack

Tool for extracting Xbox/Xbox360 recovery files.

```
Usage:
  XbRecUnpack.exe [-L/-R] <path-to-recctrl.bin> [output-folder]
  XbRecUnpack.exe [-L/-R] <path-to-remote-recovery.exe> [output-folder]
  XbRecUnpack.exe [-L/-R] <path-to-recovery.iso> [output-folder]
  XbRecUnpack.exe [-L/-R] <path-to-recovery.zip> [output-folder]
Will try extracting all files to the given output folder
If output folder isn't specified, will extract to "<input-file-path>_ext"
-L will only list files inside recovery without extracting them
-R will print info about each extracted X360 xboxrom image
  (if -R isn't used, will print a summary instead)
```
