// File: Helpers/WlanInterop.cs
using System;
using System.Runtime.InteropServices;
using System.Text;
namespace SystemOptimizer.Core.Monitoring
{
    public static class WlanInterop
    {
        private const string WlanApi = "wlanapi.dll";
        private const int WLAN_CLIENT_VERSION_XP_SP2 = 1;
        private const int WLAN_CLIENT_VERSION_LONGHORN = 2;
        [DllImport(WlanApi)]
        private static extern int WlanOpenHandle(
            uint dwClientVersion,
            IntPtr pReserved,
            out uint pdwNegotiatedVersion,
            out IntPtr phClientHandle);
        [DllImport(WlanApi)]
        private static extern int WlanEnumInterfaces(
            IntPtr hClientHandle,
            IntPtr pReserved,
            out IntPtr ppInterfaceList);
        [DllImport(WlanApi)]
        private static extern int WlanQueryInterface(
            IntPtr hClientHandle,
            ref Guid pInterfaceGuid,
            WLAN_INTF_OPCODE OpCode,
            IntPtr pReserved,
            out uint pdwDataSize,
            out IntPtr ppData,
            out WLAN_OPCODE_VALUE_TYPE pWlanOpcodeValueType);
        [DllImport(WlanApi)]
        private static extern void WlanFreeMemory(IntPtr pMemory);
        [DllImport(WlanApi)]
        private static extern int WlanCloseHandle(
            IntPtr hClientHandle,
            IntPtr pReserved);
        private enum WLAN_INTF_OPCODE : uint
        {
            wlan_intf_opcode_current_connection = 7,
        }
        private enum WLAN_OPCODE_VALUE_TYPE : uint
        {
            wlan_opcode_value_type_query_only = 0,
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct WLAN_INTERFACE_INFO_LIST
        {
            public int dwNumberOfItems;
            public int dwIndex;
            // followed by WLAN_INTERFACE_INFO[dwNumberOfItems]
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WLAN_INTERFACE_INFO
        {
            public Guid InterfaceGuid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strInterfaceDescription;
            public WLAN_INTERFACE_STATE isState;
        }
        private enum WLAN_INTERFACE_STATE : uint
        {
            wlan_interface_state_not_ready = 0,
            wlan_interface_state_connected = 1,
            // … other states omitted
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct WLAN_CONNECTION_ATTRIBUTES
        {
            public WLAN_INTERFACE_STATE isState;
            public WLAN_CONNECTION_MODE wlanConnectionMode;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string strProfileName;
            public WLAN_ASSOCIATION_ATTRIBUTES wlanAssociationAttributes;
            // other fields not needed here…
        }
        private enum WLAN_CONNECTION_MODE : uint
        {
            wlan_connection_mode_profile = 0,
            // …
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DOT11_SSID
        {
            public uint uSSIDLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] ucSSID;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct WLAN_ASSOCIATION_ATTRIBUTES
        {
            public DOT11_SSID dot11Ssid;
            // other fields omitted
        }
        public static string GetConnectedSsid()
        {
            IntPtr clientHandle = IntPtr.Zero;
            IntPtr ifaceList = IntPtr.Zero;
            try
            {
                // 1) open handle
                uint negotiated;
                int r = WlanOpenHandle(WLAN_CLIENT_VERSION_LONGHORN, IntPtr.Zero, out negotiated, out clientHandle);
                if (r != 0) return "[None]";
                // 2) enumerate interfaces
                r = WlanEnumInterfaces(clientHandle, IntPtr.Zero, out ifaceList);
                if (r != 0 || ifaceList == IntPtr.Zero) return "[None]";
                // read interface list header
                var listHeader = Marshal.PtrToStructure<WLAN_INTERFACE_INFO_LIST>(ifaceList);
                long listIterator = ifaceList.ToInt64() + Marshal.SizeOf<WLAN_INTERFACE_INFO_LIST>();
                for (int i = 0; i < listHeader.dwNumberOfItems; i++)
                {
                    // marshal each interface entry
                    var info = Marshal.PtrToStructure<WLAN_INTERFACE_INFO>(new IntPtr(listIterator));
                    listIterator += Marshal.SizeOf<WLAN_INTERFACE_INFO>();
                    if (info.isState != WLAN_INTERFACE_STATE.wlan_interface_state_connected)
                        continue;
                    // 3) query current connection on that interface
                    IntPtr connPtr;
                    uint dataSize;
                    WLAN_OPCODE_VALUE_TYPE opcode;
                    r = WlanQueryInterface(
                        clientHandle,
                        ref info.InterfaceGuid,
                        WLAN_INTF_OPCODE.wlan_intf_opcode_current_connection,
                        IntPtr.Zero,
                        out dataSize,
                        out connPtr,
                        out opcode);
                    if (r != 0 || connPtr == IntPtr.Zero)
                        continue;
                    var connInfo = Marshal.PtrToStructure<WLAN_CONNECTION_ATTRIBUTES>(connPtr);
                    // copy SSID bytes into managed array
                    var ssidBytes = new byte[connInfo.wlanAssociationAttributes.dot11Ssid.uSSIDLength];
                    Array.Copy(connInfo.wlanAssociationAttributes.dot11Ssid.ucSSID, ssidBytes, ssidBytes.Length);
                    WlanFreeMemory(connPtr);
                    // decode as UTF-8
                    return Encoding.UTF8.GetString(ssidBytes);
                }
            }
            finally
            {
                if (ifaceList != IntPtr.Zero) WlanFreeMemory(ifaceList);
                if (clientHandle != IntPtr.Zero) WlanCloseHandle(clientHandle, IntPtr.Zero);
            }
            return "[None]";
        }
    }
}
