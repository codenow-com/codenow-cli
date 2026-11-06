# Install Command

Installs the Data Plane Update Controller. The Controller then registers the
cluster with the Control Plane and deploys the Data Plane on its own schedule.

Use this command for the Update Controller model. The operator model still uses
[dp bootstrap](bootstrap.md). The two models never manage the same installation.

## Table of Contents

- [Prerequisites](#prerequisites)
- [Usage](#usage)
- [Configuration](#configuration)
- [What It Creates](#what-it-creates)
- [Permissions](#permissions)
- [After Installation](#after-installation)
- [Rerun and Recovery](#rerun-and-recovery)
- [Identity, Restore and Clone](#identity-restore-and-clone)
- [Migrating from the Operator Model](#migrating-from-the-operator-model)

## Prerequisites

- Kubernetes 1.31 or later, and storage for one 10 Gi `ReadWriteOnce` volume.
- A container registry holding `codenow/data-plane-update-controller` and
  `codenow/data-plane-deployer`, with credentials that can pull them.
- Outbound HTTPS from the cluster to the Control Plane.

## Usage

```bash
cn dp install --config ./cn-data-plane-install.json
```

The command finishes when the Controller is installed, not when the Data Plane is
deployed. The first run starts within five minutes.

| Option | Description |
|---|---|
| `--config` | Path to the installation configuration. Required. |
| `--recover-controller` | Restores the existing CronJob to `:latest`. See [Rerun and Recovery](#rerun-and-recovery). |

## Configuration

The file follows
[cn-data-plane-install.schema.json](https://github.com/codenow-com/codenow-cli/blob/main/schemas/cn-data-plane-install.schema.json).
Unknown properties are rejected, and invalid input stops the command before it
changes the cluster.

```json
{
  "controlPlaneUrl": "https://cloud.codenow.com",
  "authorizationToken": "<token>",
  "containerRegistrySystemHostname": "registry.example",
  "containerRegistrySystem": { "username": "<username>", "password": "<password>" }
}
```

| Field | Required | Description |
|---|---|---|
| `controlPlaneUrl` | Yes | Absolute HTTPS URL. |
| `authorizationToken` | Yes | Control Plane token. |
| `containerRegistrySystemHostname` | Yes | DNS name, IPv4 or bracketed IPv6, with an optional port. No scheme or path. |
| `containerRegistrySystem` | One of two | Credentials. The command creates `cn-system-image-pull-secret`. |
| `imagePullSecret` | One of two | Name of an existing pull Secret in the namespace. It is reused unchanged. |
| `httpProxy` | No | `hostname`, `port` and optional `noProxyHostnames`. |
| `namespaceName` | No | Defaults to `cn-data-plane-system`. Must equal the target namespace the Control Plane sends. |

`httpProxy` applies to Controller and Deployment Job Pods through `HTTP_PROXY`,
`HTTPS_PROXY` and `NO_PROXY`. It does not apply to image pulls: the node container
runtime pulls images, so configure its proxy separately on each node.

## What It Creates

The installation namespace, if it does not exist.

In the installation namespace: ConfigMaps `cn-dp-identity` and
`cn-dp-update-controller-config`, Secret `cn-dp-authorization`, the pull Secret,
ServiceAccount, Role, RoleBinding and CronJob `cn-dp-update-controller`. In the
cluster: ClusterRole and ClusterRoleBinding `cn-dp-update-controller`.

The CronJob is created last, with the `:latest` image. The command does not contact
the Control Plane or the registry, and creates no Job. Deployer resources belong to
the Controller.

## Permissions

The user running the command needs to create and update the resources above, and
to grant the Controller the Deployer permission profile.

Before changing anything, the command also reads the following, to find an existing
operator installation and to protect the identity:

| Resource | Verb | Scope |
|---|---|---|
| Secret `cn-data-plane-config` | `get` | Installation namespace |
| Pods | `list` | Installation namespace |
| ConfigMap `cn-dp-identity` | `get` | Installation namespace |
| ClusterRoleBinding `cn-dp-update-controller` | `get` | Cluster |

## After Installation

`cn dp dashboard` does not support this model and exits with an error. Inspect the
installation with `kubectl`, replacing `<namespace>`:

```bash
kubectl -n <namespace> get cronjob cn-dp-update-controller
kubectl -n <namespace> get jobs -l app.kubernetes.io/name=cn-dp-deployer
kubectl -n <namespace> get pods -l app.kubernetes.io/part-of=codenow-data-plane
kubectl -n <namespace> logs -l app.kubernetes.io/name=cn-dp-update-controller --tail=100
kubectl -n <namespace> logs -l app.kubernetes.io/name=cn-dp-deployer --tail=100
```

The Controller keeps its pull Secret current from the Control Plane. See
`deployment/update-controller/docs/operations.md` in the Data Plane repository for
diagnosis and repair.

## Rerun and Recovery

A rerun keeps the identity, the Controller image the Control Plane selected, and the
stored runtime settings. Everything else takes the values from the file. With
`containerRegistrySystem`, a rerun rewrites the pull Secret, so use the current
password.

`--recover-controller` puts the CronJob back on `:latest`, for example when the
selected image cannot start. It leaves identity, status, Pulumi state and the
passphrase alone. Run it only after any uncertain Controller process is confirmed
stopped; deleting a Job or Pod does not prove that.

## Identity, Restore and Clone

`cn-dp-identity` holds the installation UUID. A rerun reuses a valid one and stops on
an invalid one.

- **Restore:** to keep the registration, restore the original `cn-dp-identity` from
  backup before running the command.
- **Clone:** a copy in another cluster needs a new UUID, so do not copy this
  ConfigMap. A new UUID is a new registration.

The UUID is not a credential, and no access to `kube-system` is needed.

## Migrating from the Operator Model

The command stops with an error when the namespace holds an operator installation,
and does not take it over. Migration is a separate, manual workflow:

1. Stop operator reconciliation.
2. Transfer compatible Pulumi state, project, stack and encryption.
3. Convert the configuration.
4. Run `cn dp install`.

The operator and the Controller must never manage the same installation at once.
