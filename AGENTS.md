# AGENTS.md

Architecture and maintenance guide for the CodeNOW CLI (`cn`).

## Project Overview

A .NET 10 NativeAOT CLI for bootstrapping and managing CodeNOW Data Plane infrastructure on Kubernetes. Ships as a single binary (`cn`).

**Commands**: `cn dp bootstrap`, `cn dp dashboard`, `cn dp config encrypt`

## Architecture

```
src/CodeNOW.Cli/
├── Program.cs                          # Entry point, DI, command registration
├── Adapters/Kubernetes/                # K8s client abstraction (IKubernetesClient)
├── Common/                             # Shared utilities (YAML, JSON, Console prompts)
├── DataPlane/
│   ├── Console/Commands/               # CLI commands (BootstrapCommand, DashboardCommand, ConfigCommand)
│   ├── Console/Supports/               # Config loading, connection guard
│   ├── Console/Prompts/                # Interactive wizard
│   ├── Models/                         # OperatorConfig, enums
│   ├── Services/Provisioning/          # Core provisioning logic
│   └── Services/Operations/            # Runtime operations (dashboard, management)
```

## Commands

### `cn dp bootstrap`

Bootstraps the Data Plane: creates namespaces, installs operator, creates Pulumi stack.

| Flag | Description |
|------|-------------|
| `--config <path>` | Use existing encrypted config (requires `CN_DP_OPERATOR_ENCRYPTION_KEY` env var) |
| `--fluxcd-enable` | Install FluxCD Source Controller |
| `--fluxcd-skip-crds` | Skip FluxCD CRD installation |
| `--pulumi-skip-crds` | Skip Pulumi CRD installation |
| `--show-permissions-only` | Print minimum ClusterRole YAML and exit |

### `cn dp dashboard`

Live dashboard showing operator status, pod health, and logs. Requires active `kubectl proxy`.

### `cn dp config encrypt`

Encrypts plaintext operator configuration file. Used for CI/CD pipelines that generate config without the wizard.

## Bootstrap Flow

`BootstrapService.BootstrapAsync()` orchestrates the full bootstrap:

1. **Namespaces** — `INamespaceProvisioner` patches system/cni/ci-pipelines namespaces, creates image pull secrets.
2. **CRDs** — `IPulumiOperatorProvisioner.ApplyCrdManifestsAsync()` applies Pulumi CRDs from embedded YAML (`assets/vendor/generated/dataplane/pulumi-operator/crd/`).
3. **FluxCD** (optional) — `IFluxCDProvisioner` applies FluxCD CRDs + Source Controller.
4. **Config Secret** — `IPulumiOperatorProvisioner.CreateDataPlaneConfigSecretAsync()` creates/updates the operator config secret.
5. **Operator RBAC** — `IPulumiOperatorProvisioner.ApplyRbacManifestsAsync()` applies RBAC from embedded YAML (`assets/vendor/generated/dataplane/pulumi-operator/rbac/`).
6. **Operator Deployment** — `IPulumiOperatorProvisioner.ApplyOperatorDeploymentAsync()` applies Deployment + Service from embedded YAML (`assets/vendor/generated/dataplane/pulumi-operator/manager/`).
7. **Stack RBAC** — `IPulumiStackProvisioner.ApplyPulumiStackRbacAsync()` programmatically creates ServiceAccount, ClusterRole, ClusterRoleBinding, Roles, RoleBindings.
8. **State PVC** — `IPulumiStackProvisioner.CreatePulumiStatePvcAsync()` creates PVC for local state (when S3 disabled).
9. **Pulumi Stack** — `IPulumiStackProvisioner.ApplyPulumiStackAsync()` creates the `pulumi.com/v1` Stack custom resource.

## Embedded Manifests

Manifests under `assets/vendor/generated/dataplane/` are embedded as resources at build time (see `.csproj` `<EmbeddedResource>` items). They are read at runtime via `ProvisioningCommonTools.GetYamlDocuments()`.

| Path | Content |
|------|---------|
| `pulumi-operator/rbac/` | ClusterRoles, ClusterRoleBindings, Roles, RoleBindings, ServiceAccount for operator |
| `pulumi-operator/crd/` | Pulumi CRD definitions |
| `pulumi-operator/manager/` | Operator Deployment + Service |
| `fluxcd/` | FluxCD Source Controller manifests |

Operator resource names are prefixed with `cn-pulumi-` at runtime (`PulumiOperatorProvisioner.EnsurePrefixed()`).

## `--show-permissions-only` Feature

The `cn dp bootstrap --show-permissions-only` flag outputs a ClusterRole YAML with minimum permissions needed to run bootstrap.

### How It Works

1. `BootstrapCommand.PrintPermissions()` creates a `RecordingKubernetesClient` and runs the **actual** `BootstrapService.BootstrapAsync()` against it with a minimal `OperatorConfig`.
   - When stdin is redirected (non-interactive / CI), namespace values default to `DataPlaneConstants.DefaultSystemNamespace`, `DefaultCniNamespace`, `DefaultCiPipelinesNamespace`.
   - When running interactively, `NamespacePrompts` prompts the user for namespace values (shared with the bootstrap wizard).
2. `RecordingKubernetesClient` (`Adapters/Kubernetes/RecordingKubernetesClient.cs`) captures every K8s API call as `RecordedOperation(apiGroup, resource, verb, resourceName)`. It simulates NotFound for secrets/PVCs (so bootstrap takes the create path) and returns a ready deployment (so WaitForReady passes).
3. `BootstrapManifestPrinter.BuildDeployerClusterRole()` takes the recorded operations + applied objects and:
   - Groups operations by `(apiGroup, resource)` → collects verbs and resourceNames.
   - Secrets get special handling: scoped `get/patch/update` + unscoped `create`.
   - RBAC resources get `escalate/bind/delete` verbs plus `list/watch`.
   - Collects escalation rules from `V1ClusterRole.Rules` in applied objects.
   - Adds static rules for `pods/exec` (workspace pod) and `pods/log`.
4. `BootstrapManifestPrinter.ToYaml()` serializes to YAML.
5. In CI (release workflow), the output is written to `docs/latest/assets/bootstrap-clusterrole.yaml` and included in `docs/latest/commands/dp/bootstrap.md` via a MkDocs `pymdownx.snippets` include — no markers or post-processing needed.

### When to Update

**Automatic** — output changes when:
- Any provisioner changes what K8s API calls it makes during bootstrap.
- RBAC/CRD files change under `assets/vendor/generated/dataplane/pulumi-operator/`.
- `PulumiStackProvisioner` or `NamespaceProvisioner` adds/removes resources.

**Manual** — update when:
- A new K8s API method is added to `IKubernetesClient` — add recording in `RecordingKubernetesClient`.
- The workspace pod name pattern changes — update the `pods/exec` static rule in `BuildDeployerClusterRole()`.

### Key Files

| File | Purpose |
|------|---------|
| `Adapters/Kubernetes/RecordingKubernetesClient.cs` | Recording client + factory |
| `DataPlane/Services/Provisioning/BootstrapManifestPrinter.cs` | `BuildDeployerClusterRole()` and `ToYaml()` |
| `DataPlane/Console/Commands/BootstrapCommand.cs` | `PrintPermissions()` — orchestrates the recording run |
| `DataPlane/Console/Prompts/NamespacePrompts.cs` | Shared namespace prompts (used by wizard and `PrintPermissions()`) |
| `docs/latest/assets/bootstrap-clusterrole.yaml` | Generated YAML — committed by release workflow, included in docs via snippets |

## Dashboard Feature

The `cn dp dashboard` command displays a live TUI showing operator health, stack status, and pod logs.

### How It Works

1. `DashboardCommand` validates K8s connectivity via `KubernetesConnectionGuard`.
2. Creates a Spectre.Console `Live` layout with top (status) and bottom (logs) panels.
3. Polls `IManagementService` on a 1-second loop for:
   - `GetOperatorStatusAsync()` — operator deployment readiness.
   - `GetStackStatusAsync()` — Pulumi stack reconciliation state.
   - `GetClusterResourcesAsync()` — namespace/pod overview.
4. `LogTailer` streams pod logs from the workspace or operator pod.
5. `DashboardKeyHandler` processes keyboard input (view switching, copy, quit).

### Key Files

| File | Purpose |
|------|---------|
| `DataPlane/Console/Commands/DashboardCommand.cs` | Entry point, layout setup, main loop |
| `DataPlane/Console/Renders/DashboardRenderer.cs` | Renders status panels |
| `DataPlane/Console/Logs/LogTailer.cs` | Streams and displays pod logs |
| `DataPlane/Console/Actions/DashboardKeyHandler.cs` | Keyboard input handling |
| `DataPlane/Services/Operations/ManagementService.cs` | K8s API queries |

### When to Update

- New status fields → update `DashboardState` and `DashboardRenderer`.
- New log sources → update `LogTailer` and `LogView` enum.
- New keyboard actions → update `DashboardKeyHandler`.

## Config Encrypt Feature

The `cn dp config encrypt` command encrypts a plaintext operator configuration file for use in CI/CD.

### How It Works

1. `ConfigCommand.Encrypt(config)` validates the file exists.
2. `OperatorConfigService.EncryptConfigFile(path)`:
   - Generates a 256-bit random encryption key.
   - Deserializes the plaintext JSON via `OperatorConfigJsonContext`.
   - Re-serializes using `OperatorConfigJsonTypeInfoFactory.Create(() => key)` which encrypts annotated secret fields with AES.
   - Writes encrypted JSON back to the same file.
   - Returns the encryption key (printed to stdout).

### Key Files

| File | Purpose |
|------|---------|
| `DataPlane/Console/Commands/ConfigCommand.cs` | CLI entry point |
| `DataPlane/Services/Operations/OperatorConfigService.cs` | Encrypt logic |
| `DataPlane/Serialization/OperatorConfigJsonTypeInfoFactory.cs` | AES field encryption during serialization |
| `DataPlane/Serialization/OperatorConfigJsonContext.cs` | AOT-compatible JSON type info |

### When to Update

- New secret fields in `OperatorConfig` → annotate with the encryption attribute in the serialization model.
- Encryption algorithm change → update `OperatorConfigJsonTypeInfoFactory`.

## FluxCD Provisioning Feature

Optional FluxCD installation triggered by `--fluxcd-enable` flag during bootstrap.

### How It Works

1. `IFluxCDProvisioner.ApplyCrdManifestsAsync()` — applies FluxCD CRDs from `assets/vendor/generated/dataplane/fluxcd/`.
2. `IFluxCDProvisioner.ApplySourceControllerAsync()` — applies source-controller Deployment, Service, RBAC, and creates:
   - A `GitRepository` CR pointing to the config repo.
   - A credentials Secret (`cn-fluxcd-git-credentials`) for Git access.
3. `WaitForSourceControllerReadyAsync()` — polls until the source-controller deployment is ready.

### Key Files

| File | Purpose |
|------|---------|
| `DataPlane/Services/Provisioning/FluxCDProvisioner.cs` | CRD apply, source-controller deploy, GitRepository creation |
| `assets/vendor/generated/dataplane/fluxcd/` | Embedded FluxCD manifests |
| `DataPlane/Services/Provisioning/FluxCDInfoProvider.cs` | FluxCD image/version metadata |

### When to Update

- FluxCD version bump → update manifests under `assets/vendor/generated/dataplane/fluxcd/` and `fluxcd-info.json`.
- New FluxCD resource types → add handling in `FluxCDProvisioner`.
- Git auth changes → update credential secret construction in `ApplySourceControllerAsync()`.

## Key Provisioning Interfaces

| Interface | Responsibility |
|-----------|---------------|
| `INamespaceProvisioner` | Create/patch namespaces, image pull secrets |
| `IPulumiOperatorProvisioner` | CRDs, RBAC, Deployment from embedded manifests |
| `IPulumiStackProvisioner` | Programmatic RBAC, PVC, Pulumi Stack CR |
| `IFluxCDProvisioner` | FluxCD CRDs + Source Controller |
| `IBootstrapService` | Orchestration of all the above |

## Kubernetes Resource Application

All resources are applied via `IKubernetesClient.ApplyAsync()` which uses server-side apply (`V1Patch.PatchType.ApplyPatch`) with field manager `cn-cli`. See `KubernetesExtensions.cs` for supported kinds.

## Testing

Tests are in `tests/CodeNOW.Cli.Tests/`. Key patterns:
- `FakeKubernetesClient` — in-memory K8s client for unit tests.
- `FakePulumiOperatorProvisioner` / `FakePulumiStackProvisioner` — stubs for interface implementations.
- `BootstrapManifestPrinterTests` — verifies dynamic permission output.

## Adding a New Kubernetes Resource Type to Bootstrap

1. Add the resource creation logic in the appropriate provisioner.
2. If it's a new **kind** not already handled by `ApplyAsync`, add a case in `KubernetesExtensions.cs`.
3. Update `BuildDeployerClusterRole()` in `BootstrapManifestPrinter.cs` with a new static rule for the resource type.
4. Add/update tests in `BootstrapManifestPrinterTests.cs`.

## Adding a New CLI Command

1. Create a new class in `src/CodeNOW.Cli/DataPlane/Console/Commands/`.
2. Use `[Command("name")]` attribute on the method.
3. Register with `app.Add<YourCommand>("dp");` in `Program.cs`.
4. Add to `BannerVisibilityPolicy` if banner should be shown/hidden.
5. Inject dependencies via primary constructor — register them in `Program.cs` `ConfigureServices`.

## Adding a New Embedded Manifest Set

1. Place YAML files under `assets/vendor/generated/dataplane/<name>/`.
2. Add an `<EmbeddedResource>` entry in `CodeNOW.Cli.csproj` with a `LogicalName` pattern.
3. Read at runtime via `ProvisioningCommonTools.GetYamlDocuments(resourceRoot, relativePath, subfolder)`.
4. Parse with `YamlToJsonConverter.ConvertAll()` → get `JsonObject` per document.
5. Deserialize to typed K8s objects via `KubernetesManifestTools.DeserializeByKind()`.

## Configuration Model

`OperatorConfig` (`src/CodeNOW.Cli/DataPlane/Models/`) holds all bootstrap settings. It is serialized to/from JSON with encrypted secret fields. Encryption uses AES via `OperatorConfigJsonTypeInfoFactory` with a user-provided key.

Key sections: `Kubernetes` (namespaces, node labels, placement), `ContainerRegistry`, `Scm`, `Npm`, `S3`, `HttpProxy`, `Security`, `FluxCD`, `Pulumi`.

## Connectivity

The CLI connects to Kubernetes via `kubectl proxy` (default `http://127.0.0.1:8001`). The `KubernetesConnectionGuard` validates connectivity before executing commands that require cluster access. The `--kube-proxy-url` global flag overrides the proxy URL.
