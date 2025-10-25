using FirmwareGen.CommandLine;
using System.Diagnostics;
using System.IO;

namespace FirmwareGen
{
    public static class MainLogic
    {
        public static bool VerifyAllComponentsArePresent()
        {
            string toolDirectory = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);

            string wimlib = Path.Combine(toolDirectory, "wimlib", "wimlib-imagex.exe");
            string Img2Ffu = Path.Combine(toolDirectory, "img2ffu", "Img2Ffu.exe");
            string DriverUpdater = Path.Combine(toolDirectory, "DriverUpdater", "DriverUpdater.exe");

            if (!File.Exists(wimlib))
            {
                Logging.Log($"Some components could not be found: {wimlib}", Logging.LoggingLevel.Error);
                return false;
            }

            if (!File.Exists(Img2Ffu))
            {
                Logging.Log($"Some components could not be found: {Img2Ffu}", Logging.LoggingLevel.Error);
                return false;
            }

            if (!File.Exists(DriverUpdater))
            {
                Logging.Log($"Some components could not be found: {DriverUpdater}", Logging.LoggingLevel.Error);
                return false;
            }

            return true;
        }

        public static void GenerateWindowsFFU(GenerateWindowsFFUOptions options)
        {
            string toolDirectory = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);

            string wimlib = Path.Combine(toolDirectory, "wimlib", "wimlib-imagex.exe");
            string Img2Ffu = Path.Combine(toolDirectory, "img2ffu", "Img2Ffu.exe");
            string DriverUpdater = Path.Combine(toolDirectory, "DriverUpdater", "DriverUpdater.exe");

            const string SystemPartition = "Y:";

            DeviceProfile deviceProfile = XmlUtils.Deserialize<DeviceProfile>(options.DeviceProfile);

            string TmpVHD = CommonLogic.GetBlankVHD(deviceProfile);
            string DiskId = VolumeUtils.MountVirtualHardDisk(TmpVHD, false);
            string VHDLetter = VolumeUtils.GetVirtualHardDiskLetterFromDiskID(DiskId);

            VolumeUtils.ApplyWindowsImageFromDVD(wimlib, options.WindowsDVD, options.WindowsIndex, VHDLetter);
            VolumeUtils.PerformSlabOptimization(VHDLetter);
            VolumeUtils.ApplyCompactFlagsToImage(VHDLetter);
            VolumeUtils.MountSystemPartition(DiskId, SystemPartition);
            VolumeUtils.ConfigureBootManager(VHDLetter, SystemPartition);
            VolumeUtils.UnmountSystemPartition(DiskId, SystemPartition);

            if (deviceProfile.SupplementaryBCDCommands.Length > 0)
            {
                VolumeUtils.MountSystemPartition(DiskId, SystemPartition);

                Logging.Log("Configuring supplemental boot");
                foreach (string command in deviceProfile.SupplementaryBCDCommands)
                {
                    VolumeUtils.RunProgram("bcdedit.exe", $"{$@"/store {SystemPartition}\EFI\Microsoft\Boot\BCD "}{command}");
                }

                VolumeUtils.UnmountSystemPartition(DiskId, SystemPartition);
            }

            Logging.Log("Adding drivers");
            VolumeUtils.RunProgram(DriverUpdater, $@"-d ""{options.DriverPack}{deviceProfile.DriverDefinitionPath}"" -r ""{options.DriverPack}"" -p ""{VHDLetter}""");

            VolumeUtils.DismountVirtualHardDisk(TmpVHD);

            Logging.Log("Making FFU");
            VolumeUtils.RunProgram(Img2Ffu, $@"-i {TmpVHD} -d VenHw(860845C1-BE09-4355-8BC1-30D64FF8E63A) -l false -b 4000 -e .\img2ffu\provisioning-partitions.txt -i .\VHDX\LUN1.vhdx -d VenHw(8D90D477-39A3-4A38-AB9E-586FF69ED051) -l true -b 4000 -e .\img2ffu\provisioning-partitions.txt -i .\VHDX\LUN2.vhdx -d VenHw(EDF85868-87EC-4F77-9CDA-5F10DF2FE601) -l true -b 4000 -e .\img2ffu\provisioning-partitions.txt -i .\VHDX\LUN3.vhdx -d VenHw(1AE69024-8AEB-4DF8-BC98-0032DBDF5024) -l true -b 4000 -e .\img2ffu\provisioning-partitions.txt -i .\VHDX\LUN4.vhdx -d VenHw(D33F1985-F107-4A85-BE38-68DC7AD32CEA) -l true -b 4000 -e .\img2ffu\provisioning-partitions.txt -i .\VHDX\LUN5.vhdx -d VenHw(4BA1D05F-088E-483F-A97E-B19B9CCF59B0) -l true -b 4000 -e .\img2ffu\provisioning-partitions.txt -f ""{options.Output}\{deviceProfile.FFUFileName}"" -c {deviceProfile.DiskSectorSize * 4} -s {deviceProfile.DiskSectorSize} -p ""{string.Join(";", deviceProfile.PlatformIDs)}"" -o {options.WindowsVer} -v V2 -t ""{options.SecureBootSigningCommand}""");

            Logging.Log("Deleting Temp VHD");
            File.Delete(TmpVHD);
        }
    }
}
