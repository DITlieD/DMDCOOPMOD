using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
namespace DeathMustDieCoop
{
    public static class NativeDetour
    {
        [DllImport("kernel32.dll")]
        private static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);
        private const uint PAGE_EXECUTE_READWRITE = 0x40;
        private const int JMP_SIZE = 14; 
        private struct DetourInfo
        {
            public IntPtr OriginalAddr;
            public IntPtr ReplacementAddr;
            public byte[] SavedBytes;
            public MethodInfo OriginalMethod;
        }
        private static readonly Dictionary<IntPtr, DetourInfo> _detours = new Dictionary<IntPtr, DetourInfo>();
        public static void Hook(MethodBase original, MethodBase replacement)
        {
            RuntimeHelpers.PrepareMethod(original.MethodHandle);
            RuntimeHelpers.PrepareMethod(replacement.MethodHandle);
            IntPtr origAddr = original.MethodHandle.GetFunctionPointer();
            IntPtr replAddr = replacement.MethodHandle.GetFunctionPointer();
            byte[] saved = new byte[JMP_SIZE];
            Marshal.Copy(origAddr, saved, 0, JMP_SIZE);
            _detours[origAddr] = new DetourInfo
            {
                OriginalAddr = origAddr,
                ReplacementAddr = replAddr,
                SavedBytes = saved,
                OriginalMethod = original as MethodInfo
            };
            WriteJump(origAddr, replAddr);
        }
        public static object CallOriginal(MethodBase original, object instance, object[] args)
        {
            RuntimeHelpers.PrepareMethod(original.MethodHandle);
            IntPtr addr = original.MethodHandle.GetFunctionPointer();
            if (!_detours.TryGetValue(addr, out var info))
                return original.Invoke(instance, args);
            uint oldProtect;
            VirtualProtect(addr, (UIntPtr)JMP_SIZE, PAGE_EXECUTE_READWRITE, out oldProtect);
            Marshal.Copy(info.SavedBytes, 0, addr, JMP_SIZE);
            VirtualProtect(addr, (UIntPtr)JMP_SIZE, oldProtect, out _);
            try
            {
                return original.Invoke(instance, args);
            }
            finally
            {
                WriteJump(addr, info.ReplacementAddr);
            }
        }
        private static void WriteJump(IntPtr from, IntPtr to)
        {
            uint oldProtect;
            VirtualProtect(from, (UIntPtr)JMP_SIZE, PAGE_EXECUTE_READWRITE, out oldProtect);
            Marshal.WriteByte(from, 0, 0xFF);
            Marshal.WriteByte(from, 1, 0x25);
            Marshal.WriteInt32(from + 2, 0);
            Marshal.WriteInt64(from + 6, to.ToInt64());
            VirtualProtect(from, (UIntPtr)JMP_SIZE, oldProtect, out _);
        }
    }
}