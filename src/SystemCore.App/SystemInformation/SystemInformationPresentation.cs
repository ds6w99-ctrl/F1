using SystemCore.App.Services;
using SystemCore.Core.SystemInformation;

namespace SystemCore.App.SystemInformation;

public sealed record InformationRow(string Label, string Value);
public sealed record InformationGroup(string Title, IReadOnlyList<InformationRow> Rows, bool Expanded = true);

// Formatting stays in the presentation layer; hardware models retain raw values.
public sealed class SystemInformationPresentation(LocalizationService l)
{
    private string Unknown => l["InfoUnavailable"];
    private string Text(string? text) => InfoFormatting.Clean(text) ?? Unknown;
    private string Size(ulong? value) => value.HasValue ? InfoFormatting.Size(value.Value, l.Culture) : Unknown;
    private string Number(uint? value) => value?.ToString("N0", l.Culture) ?? Unknown;
    private string NetworkStatus(string status) => status is "Up" or "Down" or "Testing" or "Dormant" or "NotPresent" or "LowerLayerDown" ? l["InfoNet" + status] : Unknown;
    private string Date(DateTimeOffset? value) => value is { } date ? InfoFormatting.Date(date, l.Culture) : Unknown;
    private InformationRow Row(string label, string? value) => new(l["Info" + label], Text(value));
    private InformationGroup Group(string title, params InformationRow[] rows) => new(title, rows);
    private InformationGroup Missing(string title, CollectionStatus status) => Group(title,
        Row("Status", l[status switch { CollectionStatus.PermissionRequired => "InfoPermission", CollectionStatus.TimedOut => "InfoTimeout", CollectionStatus.Ready => "InfoEmpty", _ => "InfoFailed" }]));
    private string Code(string prefix, uint? code, params uint[] known) => code.HasValue && known.Contains(code.Value) ? l[$"Info{prefix}{code}"] : Unknown;

    public IReadOnlyList<InformationGroup> Build(SystemInformationSnapshot s)
    {
        var result = new List<InformationGroup>();
        var os = s.OperatingSystem.Data;
        result.Add(Group(l["InfoOverview"], Row("Device", os?.DeviceName), Row("Edition", os?.Edition),
            Row("Version", os?.Version), Row("Build", os?.Build), Row("CpuModel", s.Cpu.Data?.FirstOrDefault()?.Model),
            Row("Gpu", s.Gpu.Data is { Count: > 0 } g ? string.Join(" / ", g.Select(a => Text(a.Name))) : null),
            Row("SystemDrive", s.Storage.Data?.Volumes.FirstOrDefault(v => v.IsSystem)?.Root),
            Row("Board", s.Firmware.Data?.Boards.FirstOrDefault()?.Model)));
        result.Add(os is null ? Missing(l["InfoOs"], s.OperatingSystem.Status) : Group(l["InfoOs"],
            Row("Name", os.Name), Row("Edition", os.Edition), Row("Version", os.Version), Row("Build", os.Build),
            Row("Architecture", os.Architecture), Row("Device", os.DeviceName), Row("Installed", Date(os.Installed)),
            Row("Locale", os.SystemLocale), Row("UiLanguage", os.UiLanguage)));
        if (s.Cpu.Data is not { Count: > 0 }) result.Add(Missing(l["InfoCpu"], s.Cpu.Status));
        else foreach (var cpu in s.Cpu.Data) result.Add(Group(l["InfoCpu"], Row("Model", cpu.Model),
            Row("Manufacturer", cpu.Manufacturer), Row("Architecture", cpu.Architecture), Row("Cores", Number(cpu.Cores)),
            Row("Threads", Number(cpu.Threads)), Row("BaseClock", cpu.BaseClockHz?.ToString(l.Culture))));
        if (s.Memory.Data is not { Count: > 0 }) result.Add(Missing(l["InfoMemoryModules"], s.Memory.Status));
        else foreach (var m in s.Memory.Data) result.Add(Group(l["InfoMemoryModules"], Row("Locator", m.Locator),
            Row("Manufacturer", m.Manufacturer), Row("Capacity", Size(m.Capacity)), Row("ConfiguredSpeed", Number(m.ConfiguredSpeed)),
            Row("ReportedSpeed", Number(m.ReportedSpeed)), Row("MemoryType", m.SmbiosMemoryType switch { 20 => "DDR", 21 => "DDR2", 24 => "DDR3", 26 => "DDR4", 34 => "DDR5", 27 => "LPDDR", 28 => "LPDDR2", 29 => "LPDDR3", 30 => "LPDDR4", 35 => "LPDDR5", _ => null }),
            Row("Bank", m.Bank), Row("PartNumber", m.PartNumber)));
        if (s.Gpu.Data is not { Count: > 0 }) result.Add(Missing(l["InfoGpu"], s.Gpu.Status));
        else foreach (var gpu in s.Gpu.Data) result.Add(Group(l["InfoGpu"], Row("Name", gpu.Name), Row("Manufacturer", gpu.Vendor),
            Row("VideoProcessor", gpu.AdapterType), Row("Driver", gpu.DriverVersion), Row("DriverDate", Date(gpu.DriverDate)), Row("Vram", Size(gpu.DedicatedBytes))));
        if (s.Firmware.Data?.Boards is not { Count: > 0 } boards) result.Add(Missing(l["InfoBoard"], s.Firmware.Data?.BoardsUnavailable == true ? CollectionStatus.Unavailable : s.Firmware.Status));
        else foreach (var board in boards) result.Add(Group(l["InfoBoard"], Row("Manufacturer", board.Manufacturer), Row("Model", board.Model), Row("Version", board.Version)));
        if (s.Firmware.Data?.Bios is not { Count: > 0 } biosItems) result.Add(Missing(l["InfoBios"], s.Firmware.Data?.BiosUnavailable == true ? CollectionStatus.Unavailable : s.Firmware.Status));
        else foreach (var bios in biosItems) result.Add(Group(l["InfoBios"], Row("Manufacturer", bios.Manufacturer), Row("Version", bios.Version),
            Row("ReleaseDate", Date(bios.ReleaseDate)), Row("Smbios", bios.SmbiosVersion)));
        if (s.Storage.Data is not { } storage) result.Add(Missing(l["InfoStorage"], s.Storage.Status));
        else
        {
            if (storage.Partial) result.Add(Group(l["InfoStorage"], Row("Status", l["PartialData"])));
            if (storage.Disks.Count == 0) result.Add(Missing(l["InfoPhysicalDisks"], CollectionStatus.Ready));
            foreach (var disk in storage.Disks) result.Add(Group(l["InfoPhysicalDisks"], Row("Model", disk.Model), Row("Manufacturer", disk.Manufacturer),
                Row("Bus", Code("Bus", disk.BusType, 1, 2, 3, 7, 8, 9, 10, 11, 12, 14, 15, 16, 17, 18)),
                Row("Media", Code("Media", disk.MediaType, 3, 4, 5)), Row("Capacity", Size(disk.Capacity)), Row("Health", Code("Health", disk.Health, 0, 1, 2))));
            if (storage.Volumes.Count == 0) result.Add(Missing(l["InfoVolumes"], CollectionStatus.Ready));
            foreach (var v in storage.Volumes)
            {
                var used = v.Total.HasValue && v.Free.HasValue && v.Free <= v.Total ? v.Total - v.Free : null;
                var percent = used.HasValue && v.Total.HasValue ? InfoFormatting.Percentage(used.Value, v.Total.Value) : null;
                result.Add(Group(l["InfoVolumes"], Row("Drive", v.Root), Row("Label", v.Label), Row("FileSystem", v.FileSystem),
                    Row("Capacity", Size(v.Total)), Row("Used", Size(used)), Row("Free", Size(v.Free)), Row("Usage", percent.HasValue ? l.Format("Percent", percent.Value) : null)));
            }
        }
        if (s.Network.Data is not { Count: > 0 }) result.Add(Missing(l["InfoNetwork"], s.Network.Status));
        else foreach (var category in s.Network.Data.GroupBy(n => !n.Active ? "InfoDisconnected" : n.Physical == false ? "InfoVirtual" : "InfoActive")
            .OrderBy(g => g.Key == "InfoActive" ? 0 : g.Key == "InfoVirtual" ? 1 : 2))
        {
            var rows = new List<InformationRow>();
            foreach (var n in category) rows.AddRange((InformationRow[])[
                Row("Name", n.Name), Row("DescriptionLabel", n.Description), Row("InterfaceType", n.Type), Row("Status", NetworkStatus(n.Status)),
                Row("AdapterClass", n.Physical.HasValue ? l[n.Physical.Value ? "InfoPhysical" : "InfoVirtual"] : null),
                Row("LinkSpeed", n.Speed.HasValue ? l.Format("InfoMbps", n.Speed.Value / 1000000.0) : null),
                Row("Ipv4", string.Join(", ", n.Ipv4)), Row("Ipv6", string.Join(", ", n.Ipv6)),
                Row("Gateway", string.Join(", ", n.Gateways)), Row("Dns", string.Join(", ", n.Dns))]);
            result.Add(new(l["InfoNetwork"] + " · " + l[category.Key], rows, false));
        }
        return result;
    }
}
