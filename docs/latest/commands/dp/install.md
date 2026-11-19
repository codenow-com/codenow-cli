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
- [Controller Permissions](#controller-permissions)
- [After Installation](#after-installation)
- [Rerun and Recovery](#rerun-and-recovery)
- [Identity Loss and Clone](#identity-loss-and-clone)
- [Migrating from the Operator Model](#migrating-from-the-operator-model)

## Prerequisites

- Kubernetes 1.31 or later, and storage for one 10 Gi `ReadWriteOnce` volume.
- A container registry holding `codenow/data-plane-update-controller` and
  `codenow/data-plane-deployer`, with credentials that can pull them.
- Outbound HTTP from the cluster to the Control Plane. HTTPS is not supported yet,
  so the authorization token travels unencrypted.

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
  "controlPlaneUrl": "http://cloud.codenow.com",
  "authorizationToken": "<token>",
  "containerRegistrySystemHostname": "registry.example",
  "containerRegistrySystem": { "username": "<username>", "password": "<password>" }
}
```

| Field | Required | Description |
|---|---|---|
| `controlPlaneUrl` | Yes | Absolute HTTP URL. HTTPS is not supported yet. |
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
cluster: ClusterRole and ClusterRoleBinding `cn-dp-update-controller-<namespace>`.

A cluster holds one Data Plane installation. The command stops before changing
anything when another namespace of the cluster already holds one, and names that
namespace; rerun the command with that namespace to update the existing
installation.

The CronJob is created last, with the `:latest` image. The command does not contact
the Control Plane or the registry, and creates no Job. Deployer resources belong to
the Controller.

| CronJob setting | Value |
|---|---|
| Schedule | `*/5 * * * *` |
| `concurrencyPolicy` | `Forbid` |
| Job history | 1 successful, 3 failed |
| `backoffLimit` | `0`, `restartPolicy: Never`, no TTL |
| `activeDeadlineSeconds` | `900`, the Controller's run budget |
| `terminationGracePeriodSeconds` | `30` |
| Image | `<registry>/codenow/data-plane-update-controller:latest`, `imagePullPolicy: Always` |
| CPU / memory | request `100m` / `128Mi`, limit `500m` / `256Mi` |
| Security | UID/GID `65532`, `runAsNonRoot`, no privilege escalation, all capabilities dropped, `RuntimeDefault` seccomp (Restricted Pod Security) |

## Permissions

The user running the command needs these permissions:

| API group | Resource | Verbs | Scope |
|---|---|---|---|
| core | `namespaces` | `get`, `create` | Cluster |
| core | `serviceaccounts`, `configmaps`, `secrets` | `get`, `create`, `patch` | Installation namespace |
| `batch` | `cronjobs` | `get`, `create`, `patch` | Installation namespace |
| `rbac.authorization.k8s.io` | `roles`, `rolebindings` | `get`, `create`, `patch` | Installation namespace |
| `rbac.authorization.k8s.io` | `clusterroles`, `clusterrolebindings` | `get`, `create`, `patch` | Cluster |
| `rbac.authorization.k8s.io` | `clusterrolebindings` | `list` | Cluster, to find another installation |

The command grants the Controller the permissions in
[Controller Permissions](#controller-permissions). Kubernetes allows that only when
the user already holds every one of them, or when an administrator has granted the
user `escalate` on the Role `cn-dp-update-controller` and the ClusterRole
`cn-dp-update-controller-<namespace>` and `bind` on the same two roles. The command
never grants these to the user. A name-restricted `escalate` limits which roles the
user may change, not which permissions the user may put into them: with it, the user
can write any permission into those two roles, including the cluster-wide Argo CD
rule described below.

An existing pull Secret named by `imagePullSecret` is only read (`get`). The command
deletes nothing and needs no PVC, Job, Pod log or watch access.

Before changing anything, the command also reads the following, to find an existing
operator installation or another installation and to protect the identity:

| Resource | Verb | Scope |
|---|---|---|
| Secret `cn-data-plane-config` | `get` | Installation namespace |
| Pods | `list` | Installation namespace |
| ConfigMap `cn-dp-identity` | `get` | Installation namespace |
| ClusterRoleBindings labelled `app.kubernetes.io/part-of=codenow-data-plane` and `app.kubernetes.io/name=cn-dp-update-controller` | `list` | Cluster |
| ClusterRoleBinding `cn-dp-update-controller-<namespace>` | `get` | Cluster |

The command stops when one of those ClusterRoleBindings serves another namespace, or
when `cn-dp-update-controller-<namespace>` binds anything other than the Controller
ServiceAccount of the installation namespace.

## Controller Permissions

The command grants the Controller ServiceAccount two sets of permissions.

The Role `cn-dp-update-controller` holds its operational permissions in the
installation namespace:

| API group | Resource | Verbs | Names |
|---|---|---|---|
| `batch` | `cronjobs` | `get`, `patch` | `cn-dp-update-controller` |
| `batch` | `jobs` | `get`, `list`, `watch`, `create`, `delete` | Any |
| core | `pods` | `list`, `watch` | Any |
| core | `configmaps` | `get`, `list`, `watch`, `create`, `patch`, `delete` | Any |
| core | `persistentvolumeclaims` | `get` | `cn-dp-pulumi-state` |
| core | `persistentvolumeclaims` | `create` | Any |
| core | `secrets` | `get` | `cn-dp-authorization`, `cn-dp-pulumi-passphrase`, `cn-dp-deployer-secrets` |
| core | `secrets` | `patch` | `cn-dp-deployer-secrets` |
| core | `secrets` | `create` | Any |
| core | `serviceaccounts` | `get`, `patch` | `cn-dp-deployer` |
| core | `serviceaccounts` | `create` | Any |
| `rbac.authorization.k8s.io` | `roles`, `rolebindings` | `get`, `list`, `create`, `patch`, `delete` | Any |

Per-attempt journal ConfigMaps and their Roles and RoleBindings have generated names,
so those rules cover the whole namespace; labels and name prefixes do not limit them.
`create` cannot be limited by name in Kubernetes. The Controller reads its own Pod by
listing, so it has no Pod `get`, no `pods/log` and no Secret deletion.

The ClusterRole `cn-dp-update-controller-<namespace>` holds the cluster-scoped
operations, then the whole approved Deployer permission profile:

| API group | Resource | Verbs | Names |
|---|---|---|---|
| `rbac.authorization.k8s.io` | `clusterroles`, `clusterrolebindings` | `get`, `patch` | `cn-dp-deployer-<namespace>` |
| `rbac.authorization.k8s.io` | `clusterroles`, `clusterrolebindings` | `create` | Any |
| core | `namespaces` | `get`, `create`, `patch` | Any |
| core | `secrets` | `get`, `patch` | `cn-system-image-pull-secret` |
| core | `secrets` | `create` | Any |

The Controller creates the system namespaces and their image pull Secret
`cn-system-image-pull-secret`. The namespaces come from the Control Plane
configuration, so these rules are cluster-wide. Creating secret stores relies on the
profile. The Controller grants that
profile to the Deployer, and Kubernetes lets it create those roles only when it holds
every permission in them itself; it gets no `escalate` or `bind` override. The
name restrictions of the Role do not narrow the profile, which is additive.

The profile contains the Argo CD application controller rule (`*` API groups,
resources and verbs, and `*` non-resource URLs). The Controller and the Deployer are
therefore effectively cluster-wide administrators: literal `*` verbs include
`escalate`, `bind` and `impersonate`. The rule can be removed only when Argo CD no
longer creates that role. A release that needs permissions outside the profile needs
an administrator-applied extension before it is assigned.

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

## Identity Loss and Clone

`cn-dp-identity` holds the installation UUID. A rerun reuses a valid one and stops on
an invalid one.

- **Lost identity:** after the cluster or the namespace is lost, install afresh. The
  command generates a new UUID, which is a new registration; the original
  `cn-dp-identity` is not restored from a backup.
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
