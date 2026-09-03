using System;
using System.Runtime.InteropServices;

namespace TaskbarMusicWidget
{
    public static class VolumeController
    {
        #region COM CoreAudio Interfaces
        [ComImport]
        [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject { }

        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int NotNeeded();
            [PreserveSig]
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
        }

        [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig]
            int Activate(ref Guid id, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
        }

        [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            int RegisterControlChangeNotify(IntPtr pNotify);
            int UnregisterControlChangeNotify(IntPtr pNotify);
            int GetChannelCount(out uint pnChannelCount);
            int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
            [PreserveSig]
            int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
            int GetMasterVolumeLevel(out float pfLevelDB);
            [PreserveSig]
            int GetMasterVolumeLevelScalar(out float pfLevel);
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
            int GetMute(out bool pbMute);
        }
        #endregion

        private static IAudioEndpointVolume? GetEndpointVolume()
        {
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                // eRender = 0, eMultimedia = 1
                if (enumerator.GetDefaultAudioEndpoint(0, 1, out IMMDevice dev) == 0 && dev != null)
                {
                    Guid IID_IAudioEndpointVolume = typeof(IAudioEndpointVolume).GUID;
                    if (dev.Activate(ref IID_IAudioEndpointVolume, 1, IntPtr.Zero, out object epvObj) == 0)
                    {
                        return epvObj as IAudioEndpointVolume;
                    }
                }
            }
            catch
            {
                // Manejar entornos sin dispositivo de audio activo
            }
            return null;
        }

        /// <summary>
        /// Ajusta el volumen del sistema en tramos exactos (por defecto delta = 0.05f = 5%).
        /// Devuelve el nuevo porcentaje de volumen (0 a 100).
        /// </summary>
        public static int AjustarVolumen(float delta = 0.05f)
        {
            var epv = GetEndpointVolume();
            if (epv != null)
            {
                try
                {
                    if (epv.GetMasterVolumeLevelScalar(out float currentVol) == 0)
                    {
                        float nuevoVol = Math.Clamp(currentVol + delta, 0.0f, 1.0f);
                        // Redondear al múltiplo más cercano de 5% para evitar desfases de precisión
                        nuevoVol = (float)(Math.Round(nuevoVol * 20.0) / 20.0);

                        Guid emptyGuid = Guid.Empty;
                        epv.SetMasterVolumeLevelScalar(nuevoVol, ref emptyGuid);
                        return (int)Math.Round(nuevoVol * 100.0f);
                    }
                }
                catch
                {
                    // Ignorar fallos de audio
                }
                finally
                {
                    Marshal.ReleaseComObject(epv);
                }
            }
            return -1;
        }

        public static int ObtenerVolumenActual()
        {
            var epv = GetEndpointVolume();
            if (epv != null)
            {
                try
                {
                    if (epv.GetMasterVolumeLevelScalar(out float currentVol) == 0)
                    {
                        return (int)Math.Round(currentVol * 100.0f);
                    }
                }
                catch { }
                finally
                {
                    Marshal.ReleaseComObject(epv);
                }
            }
            return -1;
        }
    }
}

