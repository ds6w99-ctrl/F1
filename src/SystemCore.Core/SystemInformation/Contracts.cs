namespace SystemCore.Core.SystemInformation;

public interface IInfoProvider<T> { Task<T> CollectAsync(CancellationToken token); }
public interface IOperatingSystemInfoProvider : IInfoProvider<OperatingSystemInfo> { }
public interface ICpuInfoProvider : IInfoProvider<IReadOnlyList<CpuInfo>> { }
public interface IMemoryInfoProvider : IInfoProvider<IReadOnlyList<MemoryModuleInfo>> { }
public interface IGpuInfoProvider : IInfoProvider<IReadOnlyList<GpuInfo>> { }
public interface IMotherboardInfoProvider : IInfoProvider<FirmwareInfo> { }
public interface IStorageInfoProvider : IInfoProvider<StorageInfo> { }
public interface INetworkInfoProvider : IInfoProvider<IReadOnlyList<NetworkAdapterInfo>> { }
public interface ISystemInformationService { Task<SystemInformationSnapshot> CollectAsync(CancellationToken token); }
