using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;
using System.Linq;
using WarThunderChatTranslator.Entities;

namespace WarThunderChatTranslator.Services
{
    public static class AudioDeviceService
    {
        public static IReadOnlyList<AudioInputDeviceInfo> GetCaptureDevices()
        {
            using var enumerator = new MMDeviceEnumerator();
            string defaultId = null;
            try
            {
                using var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
                defaultId = defaultDevice?.ID;
            }
            catch
            {
                // The system may have no default capture endpoint even when other endpoints exist.
            }

            using var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            return devices
                .Select(device => new AudioInputDeviceInfo
                {
                    Id = device.ID,
                    Name = device.FriendlyName,
                    IsDefault = string.Equals(device.ID, defaultId, StringComparison.OrdinalIgnoreCase)
                })
                .OrderByDescending(device => device.IsDefault)
                .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }

        public static bool CaptureDeviceExists(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                try
                {
                    using var enumerator = new MMDeviceEnumerator();
                    using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
                    return device != null;
                }
                catch
                {
                    return false;
                }
            }

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                using var device = enumerator.GetDevice(deviceId);
                return device != null && device.State == DeviceState.Active && device.DataFlow == DataFlow.Capture;
            }
            catch
            {
                return false;
            }
        }

        public static float GetPeakLevel(string deviceId)
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                using var device = string.IsNullOrWhiteSpace(deviceId)
                    ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia)
                    : enumerator.GetDevice(deviceId);
                if (device == null || device.State != DeviceState.Active)
                {
                    return 0f;
                }

                return Math.Clamp(device.AudioMeterInformation.MasterPeakValue, 0f, 1f);
            }
            catch
            {
                return 0f;
            }
        }
    }
}
