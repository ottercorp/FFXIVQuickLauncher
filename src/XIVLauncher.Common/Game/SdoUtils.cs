using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.IO;
using System.Management;

namespace XIVLauncher.Common
{
    internal class SdoUtils
    {
        public static string GetDeviceId()
        {
            return string.Join(":", GetMacAddress(), GetCPUId(), GetDiskSerialNumber());
        }
        public static string GetMD5(byte[] payload)
        {
            var check = MD5.Create();
            var md5Bytes = check.ComputeHash(payload);
            return BitConverter.ToString(md5Bytes).Replace("-", string.Empty).ToUpper();
        }

        private static string GetCPUId()
        {
            var result = String.Empty;
            try
            {
                var asmCode = new CpuIdAssemblyCode();
                var cpuinfoBuffer = new byte[0x30];
                for (var i = 0; i < 3; i++)
                {
                    CpuIdAssemblyCode.CpuIdInfo info = new CpuIdAssemblyCode.CpuIdInfo();
                    asmCode.Call(i - 0x7FFFFFFE, ref info);
                    BitConverter.GetBytes(info.Eax).CopyTo(cpuinfoBuffer, 4 * 4 * i + 0);
                    BitConverter.GetBytes(info.Ebx).CopyTo(cpuinfoBuffer, 4 * 4 * i + 4);
                    BitConverter.GetBytes(info.Ecx).CopyTo(cpuinfoBuffer, 4 * 4 * i + 8);
                    BitConverter.GetBytes(info.Edx).CopyTo(cpuinfoBuffer, 4 * 4 * i + 12);
                }
                asmCode.Dispose();
                var validLength = cpuinfoBuffer.Length;
                while (cpuinfoBuffer[validLength - 1] == '\0' && validLength > 1)
                {
                    validLength--;
                }
                return GetMD5(cpuinfoBuffer.Take(validLength).ToArray());
            }
            catch
            {
                Log.Error("Failed to get CPU ID");
            }
            return result;
        }

        private static string GetMacAddress()
        {
            var result = String.Empty;
            try
            {
                var macAddress = WINGetAdaptersInfo.GetAdapters();
                return GetMD5(ASCIIEncoding.ASCII.GetBytes(macAddress));
            }
            catch
            {
                Log.Error("Failed to get MacAddress");
            }
            return result;
        }

        private static string GetDiskSerialNumber()
        {
            var result = String.Empty;
            try
            {
                ManagementObjectSearcher getPartitionsOnDisk = new
                    ManagementObjectSearcher("select * from Win32_DiskDrive");

                string hardDiskID = "";
                foreach (ManagementObject mo in getPartitionsOnDisk.Get())
                {
                    if (mo["Index"].ToString() != "0") continue;
                    hardDiskID = mo["SerialNumber"].ToString().Trim();
                    break;
                }
                result = GetMD5(ASCIIEncoding.ASCII.GetBytes(hardDiskID));
            }
            catch
            {
                Log.Error("Failed to get DiskSerialNumber");
            }
            return result;
        }
    }

    internal class WINGetAdaptersInfo
    {
        [DllImport("iphlpapi.dll")]
        private static extern int GetAdaptersInfo(IntPtr pAdapterInfo, ref Int64 pBufOutLen);

        [StructLayout(LayoutKind.Sequential)]
        private struct IP_ADDRESS_STRING
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
            public string Address;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IP_ADDR_STRING
        {
            public IntPtr Next;
            public IP_ADDRESS_STRING IpAddress;
            public IP_ADDRESS_STRING IpMask;
            public Int32 Context;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IP_ADAPTER_INFO
        {
            public IntPtr Next;
            public Int32 ComboIndex;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256 + 4)]
            public string AdapterName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128 + 4)]
            public string AdapterDescription;
            public UInt32 AddressLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] Address;
            public Int32 Index;
            public UInt32 Type;
            public UInt32 DhcpEnabled;
            public IntPtr CurrentIpAddress;
            public IP_ADDR_STRING IpAddressList;
            public IP_ADDR_STRING GatewayList;
            public IP_ADDR_STRING DhcpServer;
            public bool HaveWins;
            public IP_ADDR_STRING PrimaryWinsServer;
            public IP_ADDR_STRING SecondaryWinsServer;
            public Int32 LeaseObtained;
            public Int32 LeaseExpires;
        }

        public static string GetAdapters()
        {
            long structSize = Marshal.SizeOf(typeof(IP_ADAPTER_INFO));
            IntPtr pArray = Marshal.AllocHGlobal((int)new IntPtr(structSize));
            var macAddress = string.Empty;
            int ret = GetAdaptersInfo(pArray, ref structSize);
            if (ret == 111) // ERROR_BUFFER_OVERFLOW == 111
            {
                pArray = Marshal.ReAllocHGlobal(pArray, new IntPtr(structSize));
                ret = GetAdaptersInfo(pArray, ref structSize);
            } // if

            if (ret == 0)
            {
                // Call Succeeded
                var entry = Marshal.PtrToStructure<IP_ADAPTER_INFO>(pArray);
                var mac = new string[entry.AddressLength];
                for (int i = 0; i < entry.AddressLength; i++)
                {
                    mac[i] = $"{entry.Address[i]:X2}";
                }
                macAddress = string.Join("-", mac);
                Marshal.FreeHGlobal(pArray);
            } // if
            else
            {
                Marshal.FreeHGlobal(pArray);
                throw new InvalidOperationException("GetAdaptersInfo failed: " + ret);
            }
            return macAddress;
        } // GetAdapters
    }
    internal sealed class CpuIdAssemblyCode
        : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        internal ref struct CpuIdInfo
        {
            public uint Eax;
            public uint Ebx;
            public uint Ecx;
            public uint Edx;

        }

        [DllImport("kernel32.dll", EntryPoint = "VirtualAlloc")]
        internal static extern IntPtr VirtualAlloc(IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void CpuIDDelegate(int level, ref CpuIdInfo cpuId);
        [DllImport("kernel32.dll", EntryPoint = "VirtualFree")]
        internal static extern bool VirtualFree(IntPtr lpAddress, uint dwSize, int dwFreeType);

        private IntPtr _codePointer;
        private uint _size;
        private CpuIDDelegate _delegate;

        public CpuIdAssemblyCode()
        {
            byte[] codeBytes = (IntPtr.Size == 4) ? x86CodeBytes : x64CodeBytes;

            _size = (uint)codeBytes.Length;
            _codePointer = VirtualAlloc(IntPtr.Zero, new UIntPtr(_size), 0x1000 | 0x2000, 0x40);

            Marshal.Copy(codeBytes, 0, _codePointer, codeBytes.Length);
            _delegate = Marshal.GetDelegateForFunctionPointer<CpuIDDelegate>(_codePointer);
        }

        ~CpuIdAssemblyCode()
        {
            Dispose(false);
        }

        public void Call(int level, ref CpuIdInfo cpuInfo)
        {
            _delegate(level, ref cpuInfo);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            VirtualFree(_codePointer, _size, 0x8000);
        }

        private readonly static byte[] x86CodeBytes = {
                0x55,                   // push        ebp  
                0x8B, 0xEC,             // mov         ebp,esp
                0x53,                   // push        ebx  
                0x57,                   // push        edi

                0x8B, 0x45, 0x08,       // mov         eax, dword ptr [ebp+8] (move level into eax)
                0x0F, 0xA2,              // cpuid

                0x8B, 0x7D, 0x0C,       // mov         edi, dword ptr [ebp+12] (move address of buffer into edi)
                0x89, 0x07,             // mov         dword ptr [edi+0], eax  (write eax, ... to buffer)
                0x89, 0x5F, 0x04,       // mov         dword ptr [edi+4], ebx 
                0x89, 0x4F, 0x08,       // mov         dword ptr [edi+8], ecx 
                0x89, 0x57, 0x0C,       // mov         dword ptr [edi+12],edx 

                0x5F,                   // pop         edi  
                0x5B,                   // pop         ebx  
                0x8B, 0xE5,             // mov         esp,ebp  
                0x5D,                   // pop         ebp 
                0xc3                    // ret
                };

        private readonly static byte[] x64CodeBytes = {
                0x53,                       // push rbx    this gets clobbered by cpuid

                // rcx is level
                // rdx is buffer.
                // Need to save buffer elsewhere, cpuid overwrites rdx
                // Put buffer in r8, use r8 to reference buffer later.

                // Save rdx (buffer addy) to r8
                0x49, 0x89, 0xd0,           // mov r8,  rdx

                // Move ecx (level) to eax to call cpuid, call cpuid
                0x89, 0xc8,                 // mov eax, ecx
                0x0F, 0xA2,                 // cpuid

                // Write eax et al to buffer
                0x41, 0x89, 0x40, 0x00,     // mov    dword ptr [r8+0],  eax
                0x41, 0x89, 0x58, 0x04,     // mov    dword ptr [r8+4],  ebx
                0x41, 0x89, 0x48, 0x08,     // mov    dword ptr [r8+8],  ecx
                0x41, 0x89, 0x50, 0x0c,     // mov    dword ptr [r8+12], edx

                0x5b,                       // pop rbx
                0xc3                        // ret
                };
    }
}
