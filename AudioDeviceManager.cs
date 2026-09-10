using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TaskbarMusicWidget
{
    public record AudioDeviceItem(string Id, string Name, bool IsDefault);

    public static class AudioDeviceManager
    {
        #region CoreAudio COM Interfaces
        [ComImport]
        [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject { }

        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig]
            int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IMMDeviceCollection ppDevices);
            [PreserveSig]
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
        }

        [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            [PreserveSig]
            int GetCount(out uint pcDevices);
            [PreserveSig]
            int Item(uint nDevice, out IMMDevice ppDevice);
        }

        [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig]
            int Activate(ref Guid id, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
            [PreserveSig]
            int OpenPropertyStore(int stgmAccess, out IPropertyStore ppProperties);
            [PreserveSig]
            int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
            [PreserveSig]
            int GetState(out int pdwState);
        }

        [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            [PreserveSig] int GetCount(out uint cProps);
            [PreserveSig] int GetAt(uint iProp, out PROPERTYKEY pkey);
            [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
            [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT propvar);
            [PreserveSig] int Commit();
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct PROPERTYKEY
        {
            public Guid fmtid;
            public uint pid;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct PROPVARIANT
        {
            [FieldOffset(0)] public short vt;
            [FieldOffset(8)] public IntPtr pwszVal;
        }

        [ComImport]
        [Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
        private class CPolicyConfigClient { }

        [Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPolicyConfig
        {
            [PreserveSig] int GetMixFormat();
            [PreserveSig] int GetDeviceFormat();
            [PreserveSig] int SetDeviceFormat();
            [PreserveSig] int GetProcessingPeriod();
            [PreserveSig] int SetProcessingPeriod();
            [PreserveSig] int GetShareMode();
            [PreserveSig] int SetShareMode();
            [PreserveSig] int GetPropertyValue();
            [PreserveSig] int SetPropertyValue();
            [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, int eRole);
            [PreserveSig] int SetEndpointVisibility();
        }
        #endregion

        private static readonly PROPERTYKEY PKEY_Device_FriendlyName = new PROPERTYKEY
        {
            fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
            pid = 14
        };

        public static List<AudioDeviceItem> ObtenerDispositivosSalida()
        {
            var list = new List<AudioDeviceItem>();
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                string defaultId = "";
                if (enumerator.GetDefaultAudioEndpoint(0, 1, out IMMDevice defDev) == 0 && defDev != null)
                {
                    defDev.GetId(out defaultId);
                    Marshal.ReleaseComObject(defDev);
                }

                // eRender = 0, DEVICE_STATE_ACTIVE = 1
                if (enumerator.EnumAudioEndpoints(0, 1, out IMMDeviceCollection col) == 0 && col != null)
                {
                    col.GetCount(out uint count);
                    for (uint i = 0; i < count; i++)
                    {
                        if (col.Item(i, out IMMDevice dev) == 0 && dev != null)
                        {
                            dev.GetId(out string id);
                            string name = "Dispositivo de audio";

                            if (dev.OpenPropertyStore(0, out IPropertyStore store) == 0 && store != null)
                            {
                                var key = PKEY_Device_FriendlyName;
                                if (store.GetValue(ref key, out PROPVARIANT val) == 0 && val.pwszVal != IntPtr.Zero)
                                {
                                    name = Marshal.PtrToStringUni(val.pwszVal) ?? name;
                                }
                                Marshal.ReleaseComObject(store);
                            }

                            bool isDef = string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase);
                            list.Add(new AudioDeviceItem(id, name, isDef));
                            Marshal.ReleaseComObject(dev);
                        }
                    }
                    Marshal.ReleaseComObject(col);
                }
                Marshal.ReleaseComObject(enumerator);
            }
            catch { }

            return list;
        }

        public static bool EstablecerDispositivoPredeterminado(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return false;

            try
            {
                var policy = (IPolicyConfig)new CPolicyConfigClient();
                // Roles: 0 = eConsole, 1 = eMultimedia, 2 = eCommunications
                policy.SetDefaultEndpoint(deviceId, 0);
                policy.SetDefaultEndpoint(deviceId, 1);
                policy.SetDefaultEndpoint(deviceId, 2);
                Marshal.ReleaseComObject(policy);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
