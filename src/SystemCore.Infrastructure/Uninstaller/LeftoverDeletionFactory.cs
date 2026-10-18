using SystemCore.Core.Uninstaller;

namespace SystemCore.Infrastructure.Uninstaller;

public static class LeftoverDeletionFactory
{
    // The elevated client carries internal scan identities, so the production wiring stays here
    // instead of leaking those types into the composition root. The unprivileged constructor
    // (no elevated client) remains available for tests that never leave user-writable trees.
    public static ILeftoverDeletionService Create(LeftoverScanner scanner, ApplicationRemovalEvidence evidence,
        IInstalledApplicationInventory inventory, IRegistryBackupWriter backups)
        => new LeftoverDeletionService(scanner, evidence, inventory, backups, new WindowsElevatedLeftoverClient(), new LeftoverBackupWriter());
}
