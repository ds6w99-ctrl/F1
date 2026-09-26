namespace SystemCore.Core.SystemInformation;

public sealed record OperatingSystemInfo(string? Name, string? Edition, string? Version, string? Build,
    string Architecture, string DeviceName, DateTimeOffset? Installed, string? SystemLocale, string? UiLanguage);
public sealed record CpuInfo(string? Manufacturer, string? Model, string? Architecture, uint? Cores, uint? Threads, ulong? BaseClockHz);
public sealed record MemoryModuleInfo(string? Manufacturer, ulong? Capacity, uint? ConfiguredSpeed, uint? ReportedSpeed,
    uint? SmbiosMemoryType, string? Locator, string? Bank, string? PartNumber);
public sealed record GpuInfo(string? Name, string? Vendor, string? AdapterType, string? DriverVersion, DateTimeOffset? DriverDate, ulong? DedicatedBytes);
public sealed record MotherboardInfo(string? Manufacturer, string? Model, string? Version);
public sealed record BiosInfo(string? Manufacturer, string? Version, DateTimeOffset? ReleaseDate, string? SmbiosVersion);
public sealed record FirmwareInfo(IReadOnlyList<MotherboardInfo> Boards, IReadOnlyList<BiosInfo> Bios,
    bool BoardsUnavailable = false, bool BiosUnavailable = false);
public sealed record PhysicalDiskInfo(string? Model, string? Manufacturer, uint? BusType, uint? MediaType, ulong? Capacity, uint? Health);
public sealed record VolumeInfo(string Root, string? Label, string? FileSystem, ulong? Total, ulong? Free, bool IsSystem);
public sealed record StorageInfo(IReadOnlyList<PhysicalDiskInfo> Disks, IReadOnlyList<VolumeInfo> Volumes, bool Partial);
public sealed record NetworkAdapterInfo(string Name, string Description, string Type, string Status, bool Active,
    bool? Physical, long? Speed, IReadOnlyList<string> Ipv4, IReadOnlyList<string> Ipv6,
    IReadOnlyList<string> Gateways, IReadOnlyList<string> Dns);
public enum CollectionStatus { Ready, Unavailable, TimedOut, PermissionRequired }
public sealed record SectionResult<T>(T? Data, CollectionStatus Status);
public sealed record SystemInformationSnapshot(DateTimeOffset Collected,
    SectionResult<OperatingSystemInfo> OperatingSystem, SectionResult<IReadOnlyList<CpuInfo>> Cpu,
    SectionResult<IReadOnlyList<MemoryModuleInfo>> Memory, SectionResult<IReadOnlyList<GpuInfo>> Gpu,
    SectionResult<FirmwareInfo> Firmware, SectionResult<StorageInfo> Storage,
    SectionResult<IReadOnlyList<NetworkAdapterInfo>> Network);
