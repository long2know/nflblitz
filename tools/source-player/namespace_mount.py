#!/usr/bin/env python3
"""Mount only inside an already unshared user/mount namespace without setuid mount(8)."""

import ctypes
import os
import sys


def main(argv):
    for kind in ("user", "mnt"):
        parent = os.environ.get("SOURCE_PLAYER_PARENT_" + kind.upper() + "_NAMESPACE")
        if not parent or os.readlink("/proc/self/ns/" + kind) == parent:
            raise RuntimeError("Refusing filesystem operations without a private " + kind + " namespace.")
    libc = ctypes.CDLL(None, use_errno=True)
    operation, target, *arguments = argv
    target = os.fsencode(target)
    if operation == "unmount":
        result = libc.umount2(ctypes.c_char_p(target), 2)
    else:
        source = os.fsencode(arguments[0])
        flags = {"bind": 4096, "rbind": 4096 | 16384, "readonly": 4096 | 32 | 1,
                 "private-tmpfs": 2 | 4, "sandbox-tmpfs": 4}[operation]
        if operation == "readonly":
            inherited = os.statvfs(os.fsdecode(target)).f_flag
            flags |= inherited & (1 | 2 | 4 | 8 | 1024 | 2048)
            if inherited & getattr(os, "ST_RELATIME", 4096):
                flags |= 1 << 21
        filesystem = b"tmpfs" if operation.endswith("tmpfs") else None
        data = os.fsencode(arguments[1]) if len(arguments) > 1 else None
        result = libc.mount(ctypes.c_char_p(source), ctypes.c_char_p(target),
                            ctypes.c_char_p(filesystem), ctypes.c_ulong(flags),
                            ctypes.c_char_p(data))
    if result:
        number = ctypes.get_errno()
        raise OSError(number, os.strerror(number), os.fsdecode(target))


if __name__ == "__main__":
    main(sys.argv[1:])
