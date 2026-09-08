using System;
using System.Collections.Generic;
using OmniHid.Core.Abstractions;

namespace OmniHidTaskbar.Core
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Mock Telemetry & Peripheral Data Provider
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Provides simulated peripheral device telemetry states covering all hardware categories,
    /// power states, battery percentages, and edge cases for UI verification in debug environments.
    /// </summary>
    public static class MockDevicesProvider
    {
        /// <summary>
        /// Generates a comprehensive list of mock peripheral states showcasing all supported statuses:
        /// disconnected/sleeping, unverified long titles, inline renamed peripherals, active charging,
        /// wired full battery, and low battery alerts.
        /// </summary>
        /// <returns>A list of populated <see cref="TaskbarDeviceState"/> instances for UI testing.</returns>
        public static List<TaskbarDeviceState> GetMockDevices()
        {
            var list = new List<TaskbarDeviceState>
            {
                // 1. Headset: Disconnected / sleeping state (Tests widget error cross badge and disconnected pill)
                new TaskbarDeviceState
                {
                    Id = "MOCK_HEADSET_01",
                    Name = "Logitech PRO X 2 Lightspeed",
                    CustomName = null,
                    Category = DeviceCategory.Headset,
                    IconGlyph = TaskbarDeviceState.GetDefaultGlyph(DeviceCategory.Headset),
                    IsConnected = false,
                    BatteryPercent = -1,
                    IsCharging = false,
                    VoltageMv = 0,
                    TimeToFullMin = -1,
                    TimeToEmptyMin = -1,
                    StatusText = "Disconnected or sleeping",
                    IsWired = false,
                    IsVerified = true
                },

                // 2. Keyboard: Unverified profile + long title wrapping + discharging runtime estimate
                new TaskbarDeviceState
                {
                    Id = "MOCK_KEYBOARD_01",
                    Name = "Akko 5075B Plus Wireless RGB Multi-Host Keyboard",
                    CustomName = null,
                    Category = DeviceCategory.Keyboard,
                    IconGlyph = TaskbarDeviceState.GetDefaultGlyph(DeviceCategory.Keyboard),
                    IsConnected = true,
                    BatteryPercent = 96,
                    IsCharging = false,
                    VoltageMv = 4050,
                    TimeToFullMin = -1,
                    TimeToEmptyMin = 5760, // 96h 0m
                    StatusText = "Wireless",
                    IsWired = false,
                    IsVerified = false // Displays "Unverified" badge without clipping
                },

                // 3. Mouse: Renamed with custom alias + actively charging over USB with time-to-full estimate
                new TaskbarDeviceState
                {
                    Id = "MOCK_MOUSE_01",
                    Name = "ARDOR GAMING Prime X Wireless",
                    CustomName = "Main Gaming Mouse", // Verifies original name shown below with 0 left indent
                    Category = DeviceCategory.Mouse,
                    IconGlyph = TaskbarDeviceState.GetDefaultGlyph(DeviceCategory.Mouse),
                    IsConnected = true,
                    BatteryPercent = 65,
                    IsCharging = true,
                    VoltageMv = 3890,
                    TimeToFullMin = 45, // ~0h 45m to full
                    TimeToEmptyMin = -1,
                    StatusText = "Charging via USB",
                    IsWired = false,
                    IsVerified = true
                },

                // 4. Gamepad: Fully charged and operating over wired USB link
                new TaskbarDeviceState
                {
                    Id = "MOCK_GAMEPAD_01",
                    Name = "Xbox Wireless Controller",
                    CustomName = null,
                    Category = DeviceCategory.Gamepad,
                    IconGlyph = TaskbarDeviceState.GetDefaultGlyph(DeviceCategory.Gamepad),
                    IsConnected = true,
                    BatteryPercent = 100,
                    IsCharging = false,
                    VoltageMv = 4200,
                    TimeToFullMin = -1,
                    TimeToEmptyMin = -1,
                    StatusText = "Wired",
                    IsWired = true,
                    IsVerified = true
                },

                // 5. Mouse: Low battery warning threshold (<= 20%)
                new TaskbarDeviceState
                {
                    Id = "MOCK_MOUSE_02",
                    Name = "Razer DeathAdder V3 Pro",
                    CustomName = null,
                    Category = DeviceCategory.Mouse,
                    IconGlyph = TaskbarDeviceState.GetDefaultGlyph(DeviceCategory.Mouse),
                    IsConnected = true,
                    BatteryPercent = 12,
                    IsCharging = false,
                    VoltageMv = 3450,
                    TimeToFullMin = -1,
                    TimeToEmptyMin = 120, // 2h 0m remaining
                    StatusText = "Low Battery",
                    IsWired = false,
                    IsVerified = true
                }
            };

            var customNames = SettingsManager.Instance.Current.CustomDeviceNames;
            if (customNames != null)
            {
                foreach (var d in list)
                {
                    string alias;
                    if (!string.IsNullOrEmpty(d.Id) && customNames.TryGetValue(d.Id, out alias))
                    {
                        d.CustomName = alias;
                    }
                }
            }

            return list;
        }
    }
}
