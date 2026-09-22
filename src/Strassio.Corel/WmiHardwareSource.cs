#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using Strassio.Licensing;

namespace Strassio.Corel
{
    /// <summary>
    /// Признаки компьютера для кода лицензии (docs/SPEC.md, 13.1) через WMI — он есть в каждой Windows
    /// и входит в .NET Framework. Признак, который не удалось прочитать, остаётся пустым: при сравнении
    /// кодов он ни с чем не спорит (см. HardwareCode.Matches).
    /// </summary>
    internal sealed class WmiHardwareSource : IHardwareSource
    {
        public IReadOnlyList<string?> ReadComponents() => new[]
        {
            Query("SELECT UUID FROM Win32_ComputerSystemProduct", "UUID"),
            Query("SELECT SerialNumber FROM Win32_BaseBoard", "SerialNumber"),
            Query("SELECT ProcessorId FROM Win32_Processor", "ProcessorId"),
            SystemDiskSerial(),
        };

        /// <summary>Код этого компьютера (WMI небыстрый — вызывать один раз, лучше не в потоке окна).</summary>
        public static HardwareCode ReadCode() => HardwareCode.FromComponents(new WmiHardwareSource().ReadComponents());

        private static string? Query(string wql, string property)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(wql);
                foreach (ManagementBaseObject item in searcher.Get())
                {
                    using (item)
                    {
                        string? value = item[property]?.ToString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            return value;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // WMI выключен или нет прав — этот признак будет «неизвестно».
            }

            return null;
        }

        /// <summary>Серийный номер физического диска, на котором стоит Windows: диск C: → раздел → диск.</summary>
        private static string? SystemDiskSerial()
        {
            try
            {
                string drive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System))?.TrimEnd('\\') ?? "C:";
                using var partitions = new ManagementObjectSearcher(
                    "ASSOCIATORS OF {Win32_LogicalDisk.DeviceID='" + drive + "'} WHERE AssocClass=Win32_LogicalDiskToPartition");
                foreach (ManagementBaseObject partition in partitions.Get())
                {
                    using (partition)
                    {
                        using var disks = new ManagementObjectSearcher(
                            "ASSOCIATORS OF {Win32_DiskPartition.DeviceID='" + partition["DeviceID"] + "'} WHERE AssocClass=Win32_DiskDriveToDiskPartition");
                        foreach (ManagementBaseObject disk in disks.Get())
                        {
                            using (disk)
                            {
                                string? serial = disk["SerialNumber"]?.ToString();
                                if (!string.IsNullOrWhiteSpace(serial))
                                {
                                    return serial;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // См. Query.
            }

            return null;
        }
    }
}
