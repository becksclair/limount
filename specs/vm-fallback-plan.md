# Why Hyper-V should be the preferred VM provider

**Hyper-V is a strong default** because it's built into Windows (no separate download), integrates cleanly with the same virtualization substrate WSL uses, and is scriptable via PowerShell. However:

* **Hyper-V cannot be installed on Windows Home** (Windows 10/11 Home). ([Microsoft Learn][1])
* Enabling Hyper-V can make **other hypervisors run unreliably** when the Hyper-V hypervisor is active (Microsoft calls this out explicitly). ([Microsoft Learn][1])

So the wizard must:

* Prefer Hyper-V when available/enabled.
* Offer VMware/VirtualBox when Hyper-V is unavailable or undesired.
* Be explicit about trade-offs and required user actions.

---

# High-level goals and key decisions

## Decisions (locked-in for this plan)

1. **Primary mount backend stays WSL** by default (fast, lightweight) unless user opts into VM-default.
2. **VM fallback is opt-in**, configured via first-run wizard/settings.
3. **Fallback triggers**: primarily `XFS_UNSUPPORTED_FEATURES`, plus a small curated set of "kernel/filesystem incompatibility" classifications (expandable).
4. **Hyper-V is the preferred VM provider** when available; VMware and VirtualBox are alternative providers.
5. **Default Windows integration becomes "Network Locations"** instead of drive letters:

   * Applies to **WSL mounts** and **VM mounts**
   * Drive letters become a **Legacy/Compatibility** option (still available initially to reduce migration risk).
6. **Non-destructive by default**: VM mount flow defaults to **read-only** mounting inside the guest, and shares read-only. (WSL path can keep current behavior initially, then optionally move to read-only via setting.)

---

# Architecture changes

You currently have a single "WSL mount + map drive letter" pipeline centered on:

* `LiMount.Core/Services/MountOrchestrator.cs`
* `LiMount.Core/Services/ScriptExecutor.cs`
* `scripts/Mount-LinuxDiskCore.ps1`, `Map-WSLShareToDrive.ps1`, `Unmount-LinuxDisk.ps1`

We'll evolve into a **backend + integration** architecture:

## New conceptual layers

### A) Mount backends

* **WSL Backend** (existing logic, wrapped)
* **VM Backend** (new; uses hypervisor + guest operations)

### B) Windows integration layer (access path)

* **Network Location** (new default)
* **Drive Letter** (legacy option)
* (Optional) "None" (just show UNC and open it)

### C) Coordinator/orchestrator

A new coordinator decides:

* Try WSL first (unless "VM always")
* If WSL fails with compatible trigger -> try VM silently
* Apply Windows integration once we have an access path

---

# New and modified interfaces/models

## 1) New user settings (per-user, persisted)

Create a new settings file separate from mount state/history.

### Add

**`LiMount.Core/Models/UserSettings.cs`**

```csharp
public sealed class UserSettings
{
    public int Version { get; set; } = 1;

    public MountBackendPreference BackendPreference { get; set; } = MountBackendPreference.WslPreferred;
    public VmFallbackPolicy VmFallbackPolicy { get; set; } = VmFallbackPolicy.Disabled;

    public HypervisorSelection Hypervisor { get; set; } = HypervisorSelection.HyperVPreferred;

    public WindowsAccessMode AccessMode { get; set; } = WindowsAccessMode.NetworkLocation;

    public VmApplianceSettings VmAppliance { get; set; } = new();
    public GuestAuthSettings GuestAuth { get; set; } = new();
}
```

**New enums**

* `MountBackendPreference`: `WslPreferred`, `VmPreferred`, `VmOnly`
* `VmFallbackPolicy`: `Disabled`, `OnFsIncompatibility`, `OnSpecificErrors` (start with `OnFsIncompatibility`)
* `HypervisorSelection`: `HyperV`, `VMware`, `VirtualBox`, `Auto`
* `WindowsAccessMode`: `NetworkLocation`, `DriveLetterLegacy`, `None`

### Add

**`LiMount.Core/Interfaces/IUserSettingsService.cs`**
**`LiMount.Core/Services/UserSettingsService.cs`**

* Stores JSON in `%LocalAppData%\LiMount\settings.json`
* Supports versioned migration

## 2) Capability detection

### Add

**`LiMount.Core/Models/PlatformCapabilities.cs`**
Contains:

* `WindowsEdition` (Home/Pro/Enterprise)
* `HyperVSupported`, `HyperVEnabled`, `HyperVCmdletsAvailable`
* `WslInstalled`, `WslMountSupported`, `DefaultDistroPresent`
* `VmwareInstalled` (vmrun path), `VirtualBoxInstalled` (VBoxManage path)
* Host resources: CPU cores, RAM

### Add

**`LiMount.Core/Interfaces/IPlatformCapabilityService.cs`**
**`LiMount.Core/Services/PlatformCapabilityService.cs`**

Implementation:

* Windows edition: registry or WMI
* Hyper-V enabled: optional feature query (PowerShell/DISM) and cmdlets presence

  * Hyper-V enable command guidance in wizard uses `Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V -All` and notes reboot. ([Microsoft Learn][1])
* VMware installed: check vmrun path + maybe registry
* VirtualBox installed: check VBoxManage path

## 3) New coordinator API

### Add

**`LiMount.Core/Interfaces/IMountCoordinator.cs`**

```csharp
Task<Result<MountSession>> MountAsync(MountRequest request, CancellationToken ct = default);
Task<Result> UnmountAsync(MountSession session, CancellationToken ct = default);
```

### Add models

* **`LiMount.Core/Models/MountRequest.cs`** (disk index, partition, fsType, readOnly flag)
* **`LiMount.Core/Models/MountSession.cs`**

  * `BackendUsed` (WSL/VM)
  * `AccessPathUnc` (string)
  * `NetworkLocationName` (optional)
  * `DriveLetter` (optional legacy)
  * `MountDiagnostics` (includes WSL dmesg classification and/or VM errors)
  * `VmSessionDetails` (if VM backend used)

### Add

**`LiMount.Core/Services/MountCoordinator.cs`**

* Injects:

  * `IWslMountBackend`
  * `IVmMountBackend`
  * `IWindowsAccessService`
  * `IMountStateService` + `IMountHistoryService`
  * `IUserSettingsService`

## 4) Backends

### Add WSL backend wrapper

**`LiMount.Core/Interfaces/IWslMountBackend.cs`**
**`LiMount.Core/Services/Backends/WslMountBackend.cs`**

* Internally calls your existing `IMountScriptService` and `IUnmountScriptService` (currently `ScriptExecutor`)
* Produces `MountSession` (BackendUsed=Wsl, AccessPathUnc=MountResult.MountPathUNC, etc.)

### Add VM backend

**`LiMount.Core/Interfaces/IVmMountBackend.cs`**
**`LiMount.Core/Services/Backends/VmMountBackend.cs`**

* Uses selected hypervisor provider (Hyper-V preferred)

## 5) Hypervisor provider abstraction

### Add

**`LiMount.Core/Interfaces/IHypervisorProvider.cs`**
Key methods:

* `DetectAsync() -> ProviderCapability`
* `EnsureApplianceAsync(settings)`
* `StartVmAsync()`, `StopVmAsync(mode)`
* `AttachDiskAsync(diskIndex)` / `DetachDiskAsync(diskIndex)`
* `GetVmIpAsync()`

### Implementations

* **`LiMount.Core/Services/Virtualization/HyperVProvider.cs`** (Phase 1 VM provider)
* **`.../VmwareProvider.cs`** (Phase later)
* **`.../VirtualBoxProvider.cs`** (Phase later)

---

# Hyper-V VM fallback design

## Host orchestration with Hyper-V (PowerShell)

Core concepts we'll rely on:

* **Hyper-V must be available/enabled** (Pro/Enterprise; not Home). ([Microsoft Learn][1])
* **Pass-through disk requires the disk to be Offline** on the host and exclusively attached to the VM. ([Microsoft Learn][2])
* **Attach physical disk** using `Add-VMHardDiskDrive -DiskNumber <n>` (official cmdlet supports `-DiskNumber`). ([Microsoft Learn][3])
* VM creation/config: `New-VM`, `Set-VMMemory`, `New-VMSwitch` etc. ([Microsoft Learn][4])

### Scripts to add (Hyper-V)

Create folder: **`scripts/hyperv/`**

1. **`Get-HyperVCapabilities.ps1`** (non-elevated ok)

* Outputs key-values:

  * `HyperVSupported=1/0`
  * `HyperVEnabled=1/0`
  * `HyperVCmdlets=1/0`
  * `DefaultSwitchPresent=1/0`
  * `Notes=...`

2. **`Ensure-LiMountApplianceVm.ps1`** (elevated)

* Creates VM if missing, or validates settings if exists:

  * Gen 2
  * Dynamic memory
  * CPU count set based on heuristic
  * Network: Default Switch (simple) OR LiMount internal switch (advanced option)

3. **`Start-LiMountVm.ps1`** (elevated)
4. **`Stop-LiMountVm.ps1`** (elevated)

* `StopAction=Shutdown|Save|TurnOff` (default: Shutdown or Save depending on preference)

5. **`Attach-PhysicalDiskToVm.ps1`** (elevated)

* Inputs: `VmName`, `DiskIndex`
* Steps:

  * Capture original disk state (offline/read-only flags)
  * Take disk offline (required for pass-through) ([Microsoft Learn][2])
  * `Add-VMHardDiskDrive -VMName <vm> -DiskNumber <diskIndex>` ([Microsoft Learn][3])
  * Emit `Attached=1`, `OriginalIsOffline=...`, etc.

6. **`Detach-PhysicalDiskFromVm.ps1`** (elevated)

* Removes the specific pass-through disk from VM
* Restores disk state to original

7. **`Get-VmIpAddress.ps1`** (elevated/non-elevated depending)

* Returns best IPv4 address

### Guest operations (inside Linux VM)

We want a minimal "agent" approach.

#### In-guest agent responsibilities

* Identify attached disk device (ideally the only extra disk)
* Mount the requested partition (read-only default)
* Ensure Samba share exports the mount root
* Return the share subpath

#### Provisioning approach

You have two viable routes; I'm choosing one as default, keeping the other as fallback:

**Default (recommended): LiMount Appliance VM**

* Ship (or download on demand) a small Ubuntu-based VHDX with:

  * `openssh-server`
  * `samba`
  * filesystem tools (`xfsprogs`, `btrfs-progs`, `e2fsprogs`, etc.)
  * LiMount agent installed at `/usr/local/bin/limount-agent`
* Preconfigured `limount` user and SSH key or credential bootstrap

**Fallback: "Use existing VM"**

* Wizard asks for SSH host/user/auth and runs a one-time "bootstrap script" over SSH (or instructs user)

---

# Windows access: switch from drive letters to Network Locations

## Decision

Make **Network Locations** the default integration for both WSL and VM backends.

### Why

* Avoids consuming drive letters
* Avoids complexity of `subst` / scheduled task / admin vs user session edge cases
* Keeps Explorer-friendly access without a "system drive letter" feel

### How (Windows Network Location shortcut)

Explorer's network location shortcuts are implemented as a read-only folder containing:

* `desktop.ini` with a specific CLSID2
* `target.lnk` pointing to the UNC target
  This is documented well enough to implement reliably. ([GitHub][5])

## New scripts to add

Create folder: **`scripts/network/`**

1. **`Create-NetworkLocation.ps1`** (non-elevated)

* Inputs: `Name`, `TargetUNC`
* Writes under:

  * `%APPDATA%\Microsoft\Windows\Network Shortcuts\<Name>\`
* Creates:

  * `desktop.ini` with required content
  * `target.lnk` pointing to UNC
* Marks folder read-only
* Outputs: `Success=1`, `NetworkLocationPath=...`, `TargetUNC=...`

2. **`Remove-NetworkLocation.ps1`** (non-elevated)

* Deletes that folder
* Outputs: `Success=1`

## Core integration changes

### Add

**`LiMount.Core/Interfaces/IWindowsAccessService.cs`**

```csharp
Task<Result<WindowsAccessInfo>> CreateAccessAsync(WindowsAccessRequest req, CancellationToken ct);
Task<Result> RemoveAccessAsync(WindowsAccessInfo access, CancellationToken ct);
```

### Implementations

* `NetworkLocationAccessService` (new default)
* `DriveLetterAccessService` (wrap existing mapping scripts; legacy)
* `NullAccessService` (none)

### Update

* `MountSession` stores `AccessPathUnc` always, plus `NetworkLocationName` and/or `DriveLetter`.

### WinUI "Open in Explorer"

Instead of opening `X:\`, open:

* `AccessPathUnc` directly, or
* open `shell:NetworkPlacesFolder` (optional enhancement)

---

# First-run (and missing-settings) wizard UX

## Trigger

On app startup (or when `settings.json` missing/invalid):

* Show a **Setup Wizard dialog**
* User must select:

  * default backend policy
  * hypervisor provider
  * access mode
  * VM configuration (if VM involved)

## UX design (single dialog with steps)

Add new WinUI ContentDialog-based wizard:

### Step 1: "Detect your environment"

Shows a capability matrix:

* WSL2

  * Installed?
  * `wsl --mount` supported?
  * Distro present?

* Hyper-V (Preferred)

  * Windows edition supports it? (Pro/Enterprise) ([Microsoft Learn][1])
  * Hyper-V enabled?
  * Cmdlets available?

* VMware Workstation

  * vmrun detected?
  * (Optional) version

* VirtualBox

  * VBoxManage detected?

Actions (contextual):

* If Windows Home: Hyper-V greyed with text "Requires Pro/Enterprise", link to Microsoft doc. ([Microsoft Learn][1])
* If Pro but disabled: show button **Enable Hyper-V** (runs `Enable-WindowsOptionalFeature...` + "restart required" notice). ([Microsoft Learn][1])
* If VMware not installed: HyperlinkButton "Download VMware Workstation" (official VMware page / Broadcom portal guidance). ([VMware][6])
* If VirtualBox not installed: HyperlinkButton "Download VirtualBox" (official downloads). ([VirtualBox][7])

Also show a small warning note:

* "Hyper-V enabled may affect VMware/VirtualBox VM reliability" (Microsoft explicitly mentions this). ([Microsoft Learn][1])

### Step 2: "Choose mount strategy"

Radio group:

1. **WSL first, VM fallback for incompatibilities** (recommended)
2. **Always use VM appliance** (max compatibility)
3. **WSL only** (disable VM features)

Checkbox:

* ✅ "Fallback silently when WSL reports filesystem incompatibility" (opt-in, default checked if VM configured)

### Step 3: "Choose hypervisor provider"

Radio group with disabled options:

* Hyper-V (recommended if available)
* VMware Workstation (if installed)
* VirtualBox (if installed)

Unavailable providers are greyed out with reason + install link.

### Step 4: "VM appliance setup"

If VM is involved:

* Option A: "Create LiMount appliance VM" (recommended)
* Option B: "Use existing VM" (advanced)

Hyper-V specifics:

* VM name (default `LiMount-Appliance`)
* Storage location for VHDX
* Network mode:

  * "Default Switch (simple)" (recommended)
  * "Isolated internal switch (advanced)" (stable IP, more setup)
* Resources (auto-recommended):

  * vCPU: `min(2, max(1, hostCores/4))`
  * RAM: dynamic, startup 1–2GB, max 2–4GB depending on host RAM
  * Auto-stop: Shutdown (default) or Save (fast resume)

### Step 5: "Windows access integration"

* Default selection: **Network Location**
* Legacy option: Drive letter mapping (shows compatibility note)
* "None" option

### Step 6: Summary

Displays:

* Primary backend
* Fallback behavior
* Hypervisor provider
* Access mode
* "Test configuration" button

---

# Trigger conditions for fallback

You already classify unsupported XFS as `XFS_UNSUPPORTED_FEATURES`.

## Decision: fallback triggers (Phase 1)

Fallback to VM when:

* `MountResult.ErrorCode == XFS_UNSUPPORTED_FEATURES`
* OR (future) a broader category `FS_KERNEL_INCOMPATIBLE` (e.g., Btrfs features, ZFS, etc.)

### Silent fallback behavior

If user enabled fallback:

1. Attempt WSL mount
2. If it fails with compatible trigger:

   * Run a best-effort `wsl --unmount` to ensure disk isn't left attached (you already treat ERROR_FILE_NOT_FOUND as OK)
   * Attempt VM mount
3. If VM mount succeeds:

   * UI shows success with a subtle indicator: "Mounted via VM fallback"
   * No modal dialog

If VM mount fails:

* Return a combined error explaining:

  * WSL error (including dmesg summary)
  * VM error (hypervisor/guest steps and hints)

---

# VM orchestration details: Hyper-V preferred path

## Mount flow (VM backend)

**Goal:** result is a UNC path that Windows can browse and a network location created for it.

### Steps (host + guest)

1. Validate settings + capability:

   * Hyper-V supported and enabled ([Microsoft Learn][1])
2. Ensure VM exists (create if needed):

   * `New-VM` + configure memory (`Set-VMMemory`) + switch (`New-VMSwitch` if needed) ([Microsoft Learn][4])
3. Start VM
4. Attach disk pass-through:

   * Take disk offline on host (required) ([Microsoft Learn][2])
   * `Add-VMHardDiskDrive -DiskNumber <n>` ([Microsoft Learn][3])
5. Guest mount:

   * SSH to VM
   * Run `limount-agent mount --partition <p> --readonly`
6. Guest share:

   * Ensure samba share present (read-only default)
7. Host discovers VM IP and forms UNC:

   * `\\<ip>\limount\<diskXpY>` (or similar)
8. Create Windows network location shortcut pointing to UNC
9. Update state/history

## Unmount flow (VM backend)

1. Remove Windows network location
2. SSH unmount in guest
3. Detach disk from VM
4. Restore host disk online/offline state to original
5. Stop VM (policy: shutdown/save)

---

# Secure credential/auth handling

## Decision: use Windows Credential Manager + SSH keys

### Host side

* Store secrets using **Windows Credential Manager** (or DPAPI-backed storage)
* Never pass secrets on command line when possible
* Redact from logs

### Guest access

Preferred:

* SSH key authentication for agent commands
* Samba:

  * Either guest read-only share limited to VM internal network
  * Or user+password stored in Credential Manager

Wizard offers:

* "Use secure random generated credentials" (default for appliance)
* "Use my existing VM credentials" (manual entry + Test)

---

# State model updates (and startup restore)

## Update models

### Modify

**`LiMount.Core/Models/ActiveMount.cs`**

* Make drive letter optional
* Add:

  * `BackendUsed`
  * `AccessPathUNC` (always)
  * `NetworkLocationName` (optional)
  * VM fields (optional): `HypervisorProvider`, `VmName`, etc.

### Modify

**`LiMount.Core/Models/MountHistoryEntry.cs`**

* Add `BackendUsed`, `AccessMode`, `AccessPathUNC`
* Keep old fields for migration

### Modify

`LiMount.Core/Serialization/LiMountJsonContext.cs`

* Update source-gen serialization for new fields

## Startup detection / restore

Update **`LiMount.Core/ViewModels/BaseMainViewModel.cs`**:

* Before environment validation, ensure settings exist; show wizard if missing.
* Replace "drive letter required" assumptions:

  * For NetworkLocation mode: show "Open" based on `AccessPathUNC`
* Restore logic:

  * WSL mounts: keep live mount table check (already fixed)
  * VM mounts: verify `AccessPathUNC` reachable; if unreachable but VM is configured, optionally attempt "Reconnect" (start VM + re-check)

---

# Concrete file-by-file plan (phased)

## Phase 0: Foundations (settings + capability detection + UI wizard skeleton)

**Core**

* Add:

  * `LiMount.Core/Models/UserSettings.cs`
  * `LiMount.Core/Interfaces/IUserSettingsService.cs`
  * `LiMount.Core/Services/UserSettingsService.cs`
  * `LiMount.Core/Models/PlatformCapabilities.cs`
  * `LiMount.Core/Interfaces/IPlatformCapabilityService.cs`
  * `LiMount.Core/Services/PlatformCapabilityService.cs`

**WinUI**

* Add:

  * `LiMount.WinUI/Views/SetupWizardDialog.xaml` (+ code-behind)
  * `LiMount.WinUI/ViewModels/SetupWizardViewModel.cs`
  * `LiMount.WinUI/Services/HyperlinkNavigationService.cs` (optional helper)
* Modify:

  * `LiMount.WinUI/Views/MainWindow.cs` (on load: check settings; show wizard)
  * `LiMount.WinUI/App.xaml.cs` (DI registrations)

**Docs**

* Add:

  * `docs/setup/virtualization-setup.md`
  * `docs/setup/network-locations.md`

## Phase 1: Network Locations integration (WSL path first)

**Core**

* Add:

  * `LiMount.Core/Interfaces/IWindowsAccessService.cs`
  * `LiMount.Core/Models/WindowsAccessInfo.cs`, `WindowsAccessRequest.cs`
  * `LiMount.Core/Services/Access/NetworkLocationAccessService.cs`
  * `LiMount.Core/Services/Access/DriveLetterAccessService.cs` (wrap existing scripts)
* Modify:

  * `LiMount.Core/Services/MountOrchestrator.cs` to call `IWindowsAccessService` instead of `IDriveMappingService`
  * `LiMount.Core/Services/UnmountOrchestrator.cs` similarly
  * `LiMount.Core/ViewModels/BaseMainViewModel.cs` UI assumptions about drive letter selection
  * `LiMount.WinUI/Views/MainPage.xaml` (hide drive letter selection unless AccessMode==DriveLetterLegacy)
  * `LiMount.WinUI/ViewModels/MainViewModel.cs` open explorer via UNC instead of drive letter

**Scripts**

* Add:

  * `scripts/network/Create-NetworkLocation.ps1`
  * `scripts/network/Remove-NetworkLocation.ps1`

**Tests**

* Update deterministic UI tests to no longer require drive letter selection by default:

  * `LiMount.UITests/MainPageUiTests.cs`
* Update test mode to simulate network location mode.

## Phase 2: Hyper-V provider (host orchestration)

**Core**

* Add:

  * `LiMount.Core/Interfaces/IHypervisorProvider.cs`
  * `LiMount.Core/Services/Virtualization/HyperVProvider.cs`
  * `LiMount.Core/Models/VmApplianceSettings.cs`

**Scripts**

* Add:

  * `scripts/hyperv/*` scripts described above (capabilities, ensure vm, start/stop, attach/detach, get ip)

**Docs**

* Add:

  * `docs/vm-fallback/hyperv.md` (requirements, enabling, troubleshooting)

## Phase 3: Guest agent + SMB share path

**Repo**

* Add folder:

  * `appliance/` (build assets + agent scripts)
  * `appliance/limount-agent/limount-agent.sh`
  * `appliance/cloud-init/` (if using cloud images)

**Core**

* Add:

  * `LiMount.Core/Services/Virtualization/SshGuestExecutor.cs`
  * `LiMount.Core/Interfaces/IGuestMountAgent.cs`
  * `LiMount.Core/Services/Virtualization/GuestMountAgent.cs`

**Settings**

* Extend wizard to configure guest auth and "Test Connection"

## Phase 4: MountCoordinator + silent fallback logic

**Core**

* Add:

  * `LiMount.Core/Interfaces/IMountCoordinator.cs`
  * `LiMount.Core/Services/MountCoordinator.cs`
  * `LiMount.Core/Services/Backends/WslMountBackend.cs`
  * `LiMount.Core/Services/Backends/VmMountBackend.cs`
* Modify:

  * `BaseMainViewModel` to use `IMountCoordinator` instead of `IMountOrchestrator`
  * Update state/history models to include backend + access mode

**Behavior**

* If WSL mount fails with `XFS_UNSUPPORTED_FEATURES`, automatically try VM mount if configured.
* Continue without modal prompts; status line indicates fallback happened.

## Phase 5: VMware and VirtualBox providers (optional)

**VMware**

* Use vmrun for lifecycle + guest ops docs. ([Broadcom TechDocs][8])
* Disk attach will likely require config edits or raw disk mapping; implement later.

**VirtualBox**

* Implement raw disk access with VBoxManage `createrawvmdk`.
* Strong warnings: raw disk access is expert-only and can cause data loss. ([Oracle Docs][9])

Wizard supports selecting these providers; initially can be "experimental" behind flag.

---

# Failure handling and cleanup (must not brick disks)

## Non-destructive defaults

* VM backend mounts read-only by default.
* SMB share read-only by default.

## Cleanup strategy (always)

* Use a "reverse-order unwind" stack:

  * Remove Windows access (network location)
  * Guest unmount
  * Detach disk from VM
  * Restore host disk flags (offline/online/read-only)
  * Stop VM (policy)

## Edge cases to explicitly handle

* Disk already offline before mount: preserve original state
* Disk cannot be offlined (in use): fail early with actionable hint
* VM start failure: fail with provider diagnostics
* Guest unreachable: show "VM running but SSH not reachable" hints
* SMB credentials invalid: show "share reachable but auth failed"

---

# Testing strategy

## Deterministic simulated tests (CI-friendly)

* Extend `LiMount.WinUI/TestMode` to simulate:

  * WSL unsupported XFS error then VM success
  * VM failure modes (hypervisor missing, guest auth failure, share missing)
* Unit tests for:

  * Fallback decision logic
  * State updates with backend/access mode
  * Network location create/remove parsing

## Hardware-in-loop (real VM + real disk)

Add script:

* **`scripts/run-hil-vm-fallback-test.ps1`**

  * Detects Hyper-V enabled
  * Ensures VM appliance exists
  * Uses real disk with known unsupported XFS partition:

    * Validate WSL fails with `XFS_UNSUPPORTED_FEATURES`
    * Validate VM fallback succeeds
    * Validate Windows UNC access and cleanup

Also update:

* `docs/testing/integration-tests.md` with VM HIL steps

---

# Rollout strategy (phased + feature flags)

## Feature flags (config + user settings)

* `EnableNetworkLocations` (default on for new installs, off for existing initially)
* `EnableVmFallback` (default off until wizard opts in)
* `EnableHyperVProvider` (default on if Hyper-V available, but gated by wizard)
* `EnableVmwareProvider` / `EnableVirtualBoxProvider` (experimental)

## Release plan

1. Release Network Locations as optional (keep drive letters default for existing users)
2. Release Setup Wizard + capabilities detection (no VM yet)
3. Release Hyper-V backend as experimental
4. Enable "silent fallback on XFS_UNSUPPORTED_FEATURES" for opted-in users
5. Deprecate drive letters gradually (keep legacy option for power users)

---

# Documentation updates required

### Update

* `README.md`

  * Replace "drive letters by default" with "Network Locations by default"
  * Add VM fallback overview + Hyper-V requirements (Pro/Enterprise) ([Microsoft Learn][1])

### Add

* `docs/setup/first-run-wizard.md`
* `docs/vm-fallback/hyperv.md`
* `docs/setup/network-locations.md`
* `docs/security/credentials.md`
* `docs/testing/hil-vm-fallback.md`

### Update incident follow-up

* `docs/incidents/2026-02-11-wsl-xfs-mount-regression.md`

  * Link to VM fallback docs as the remediation path

---

[1]: https://learn.microsoft.com/en-us/windows-server/virtualization/hyper-v/get-started/install-hyper-v "https://learn.microsoft.com/en-us/windows-server/virtualization/hyper-v/get-started/install-hyper-v"
[2]: https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2012-r2-and-2012/dn803924%28v%3Dws.11%29 "https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2012-r2-and-2012/dn803924%28v%3Dws.11%29"
[3]: https://learn.microsoft.com/en-us/powershell/module/hyper-v/add-vmharddiskdrive?view=windowsserver2025-ps "https://learn.microsoft.com/en-us/powershell/module/hyper-v/add-vmharddiskdrive?view=windowsserver2025-ps"
[4]: https://learn.microsoft.com/en-us/powershell/module/hyper-v/new-vm?view=windowsserver2025-ps "https://learn.microsoft.com/en-us/powershell/module/hyper-v/new-vm?view=windowsserver2025-ps"
[5]: https://github.com/files-community/Files/issues/11611 "Feature: Support FileFolder / Folder Shortcuts / Network Locations shortcuts · Issue #11611 · files-community/Files · GitHub"
[6]: https://www.vmware.com/products/desktop-hypervisor/workstation-and-fusion?utm_source=chatgpt.com "Fusion and Workstation"
[7]: https://www.virtualbox.org/wiki/Downloads "https://www.virtualbox.org/wiki/Downloads"
[8]: https://techdocs.broadcom.com/us/en/vmware-cis/desktop-hypervisors/workstation-pro/17-0/using-vmware-workstation-pro/using-the-vmrun-command-to-control-virtual-machines.html?utm_source=chatgpt.com "Using the vmrun Command to Control Virtual Machines"
[9]: https://docs.oracle.com/en/virtualization/virtualbox/6.0/admin/adv-storage-config.html "2.8. Advanced Storage Configuration"
