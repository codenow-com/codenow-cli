# Bootstrap Command

Bootstraps the CodeNOW Data Plane by preparing cluster namespaces, installing
the operator, and creating the Pulumi stack required for deployment.

![Data Plane bootstrap flow](../../assets/dataplane-bootstrap.svg)

The diagram illustrates the bootstrap lifecycle triggered by `dp bootstrap`:

- The CLI creates the system namespaces required for CodeNOW Data Plane
  components.
- The CodeNOW Pulumi operator is installed into the system namespace.
- A Pulumi stack is created to represent the Data Plane installation.
- Once the command finishes, the Pulumi operator continues asynchronously:
  it downloads the Pulumi program from the NPM registry and applies the Git
  configuration to install and reconcile the Data Plane.

## Table of Contents

- [Usage](#usage)
- [Configuration Sources](#configuration-sources)
  - [Generate via Wizard](#generate-via-wizard)
  - [Use an Existing Configuration](#use-an-existing-configuration)
    - [Interactive Selection](#interactive-selection)
    - [Command Line](#command-line)
- [Bootstrap Execution](#bootstrap-execution)

## Usage

Run the command with:

```bash
cn dp bootstrap
```

Additional flags:

- `--fluxcd-enable`: Enable installation of FluxCD components (currently installs only the [Source Controller](https://fluxcd.io/flux/components/source/)). Applies only when generating a new config.
- `--fluxcd-skip-crds`: Skip FluxCD CRD installation when FluxCD is enabled. Applies only when generating a new config.
- `--pulumi-skip-crds`: Skip Pulumi operator CRD installation. Applies only when generating a new config.

> FluxCD is required to correctly load Git configuration from Azure DevOps Server.
> It also enables advanced Git capabilities such as multiple branches, excluding
> files from the source, verifying source revisions using signatures, and more.

## Configuration Sources

The operator configuration defines how the Data Plane is provisioned, including
credentials, namespaces, and deployment settings used by the operator.

Choose one of the following configuration paths:

- Generate a new configuration via the interactive wizard.
- Use an existing configuration file.

### Generate via Wizard

Use the interactive wizard when you want guided, step-by-step configuration
creation. The wizard collects all required inputs, validates them as you go,
and produces a ready-to-use `cn-data-plane-operator.json`.

Start the flow with `cn dp bootstrap`, then choose
_"Generate cn-data-plane-operator.json using the setup wizard"_.

The wizard opens a structured form with the sections below. Work through each
section in order, using the provided descriptions and general defaults to
complete the fields accurately.

When the wizard completes, the configuration is saved as a JSON file and all
secret values are automatically encrypted. The encryption key is shown to the
user and copied to the clipboard; store it in a secure secret store because it
is required to decrypt the configuration later.

You can edit the generated file manually or use the form-driven editor via the
_"Use an Existing Configuration"_ flow. Manual edits should be limited to
non-secret values. The JSON file also includes a JSON Schema reference, which
enables code completion and validation in IDEs that support schema-based
configuration.

### Environment

| Field | Description |
| --- | --- |
| Environment name | Label for the environment / cluster (for example, "prod" or "staging"). |

### NPM Registry

| Field | Description |
| --- | --- |
| URL | Base URL of the NPM registry that hosts the Data Plane Pulumi program. |
| Access token | Token used to authenticate to the NPM registry when pulling the program. |

### Container Registry

| Field | Description |
| --- | --- |
| Hostname | Container registry hostname used to pull CodeNOW OCI images. |
| Username | Registry username with pull permissions. |
| Password | Registry password or token for the provided username. |

### SCM (Git)

| Field | Description |
| --- | --- |
| Configuration repository URL | Git URL containing the Data Plane configuration repository. The repository must include a folder that matches the specified environment name. |
| Authentication method | Select whether to use username/password or an access token for Git access. |
| Username | Git username (required for username/password auth). |
| Password | Git password (required for username/password auth). |
| Access token | Git access token (required for token-based auth). |

> When FluxCD is enabled, authentication values must be provided according to the
> FluxCD Source Controller documentation for GitRepository secrets (see the
> [secret reference](https://fluxcd.io/flux/components/source/gitrepositories/#secret-reference)).

### Kubernetes

Namespaces:

| Field | Description |
| --- | --- |
| System namespace | Namespace for core Data Plane system components. |
| CNI namespace | Namespace dedicated to networking (CNI) components. |
| CI Pipelines namespace | Namespace used by CI pipeline components. |

> Tip: If you do not want separate CNI or CI Pipelines namespaces, set their
> names to the same value as the system namespace.

Node labels:

These labels control placement of CodeNOW Data Plane system components and
application components within the cluster. We recommend using two separate
node pools so system workloads and application workloads are isolated for
capacity planning, upgrades, and operational safety.

| Field | Description |
| --- | --- |
| System label key | Node label key used to target Data Plane system workloads. |
| System label value | Node label value used to target Data Plane system workloads. |
| Application label key | Node label key used to target application workloads. |
| Application label value | Node label value used to target application workloads. |

Pod placement:

Pod placement controls how system and application workloads are scheduled on
nodes. Choose one of the following strategies:

- **Pod node selector**: Uses the Kubernetes
  [PodNodeSelector admission controller](https://kubernetes.io/docs/reference/access-authn-authz/admission-controllers/#podnodeselector)
  to enforce node selection based on labels.
- **Node selector + taints**: Combines node selectors with
  [taints and tolerations](https://kubernetes.io/docs/concepts/scheduling-eviction/taint-and-toleration/)
  for stricter isolation between system and application workloads. The taint
  must use the `NoExecute` effect.

> Note: Pod node selector is not supported on all managed Kubernetes offerings.
> Verify that it is a valid option for your cloud provider. For example, it is
> not supported on AWS EKS, while OpenShift supports it. If taints cannot be
> used for any reason, pod node selector may be the only viable option.

| Field | Description |
| --- | --- |
| Mode | Workload placement strategy for system/app pods (node selector only or selector plus taints). |

Storage:

| Field | Description |
| --- | --- |
| Storage class | Storage class used for persistent volumes; leave empty to use the cluster default. |

Security context:

| Field | Description |
| --- | --- |
| RunAs UID/GID | UID/GID for running containers in the operator-managed workloads. Setting a valid range may be required for OpenShift [Security Context Constraints](https://docs.openshift.com/container-platform/latest/authentication/managing-security-context-constraints.html); otherwise the default is typically sufficient. |

### S3 Storage

| Field | Description |
| --- | --- |
| Enable | Toggle S3-backed storage for the Data Plane. |
| URL | Endpoint URL for the S3-compatible storage service. |
| Bucket name | S3 bucket used for storing Data Plane state and artifacts. |
| Region | Region of the bucket (for example, `eu-central-1`). |
| Authentication method | Choose access keys or IAM role authentication. IAM roles apply in AWS environments to authenticate against AWS resources without static keys. |
| Access key | S3 access key ID (required for access key auth). |
| Secret key | S3 secret access key (required for access key auth). |
| IAM role | IAM role ARN used for S3 access (required for role auth). |

### HTTP Proxy

| Field | Description |
| --- | --- |
| Enable | Toggle HTTP proxy settings for outbound connectivity. |
| Hostname | Proxy hostname or IP address. |
| Port | Proxy port number. |
| No proxy hostnames | Comma-separated list of hosts that should bypass the proxy. |

> Note: Configure HTTP proxy settings when your Kubernetes cluster uses a
> corporate proxy for outbound connectivity.

### Security

| Field | Description |
| --- | --- |
| Use custom CA | Enable a custom CA bundle for TLS validation. |
| Custom CA certificate path | Path to a PEM/CRT file containing your CA certificate. |

### Configuration Output

| Field | Description |
| --- | --- |
| Configuration file path | Destination path for the generated configuration file. |
| Overwrite existing file | Confirm overwrite if the file already exists. |

After completing the wizard, continue to the final step described in
[Bootstrap Execution](#bootstrap-execution).

---

### Use an Existing Configuration

Provide a previously generated configuration file and supply its encryption
key. Choose one of the following entry points:

#### Interactive Selection

Run `cn dp bootstrap` and select:
_"Use an existing cn-data-plane-operator.json"_.

The CLI first asks for the configuration file path and then requests the
encryption key (with a limited number of attempts). After the configuration is
successfully decrypted, you can choose to edit it. Selecting edit opens the
wizard with all fields prefilled from the existing configuration, allowing you
to review and update settings before bootstrapping.

When the configuration is ready, proceed to
[Bootstrap Execution](#bootstrap-execution).

#### Command Line

Use the command line when you want a fully non-interactive flow. Provide the
configuration path via `--config` and set the encryption key using the
`CN_DP_OPERATOR_ENCRYPTION_KEY` environment variable before running the
command. This approach is ideal for automation, such as CI pipelines.

```bash
export CN_DP_OPERATOR_ENCRYPTION_KEY="your-secret-key"
cn dp bootstrap --config /path/to/cn-data-plane-operator.json
```

Once the command starts, it transitions into
[Bootstrap Execution](#bootstrap-execution).

---

## Bootstrap Execution

After configuration is provided (via the wizard or an existing file), the CLI
starts the installation and waits for a successful completion. During this
phase, the bootstrap process:

- Creates and prepares required namespaces and resources.
- Deploys the operator and establishes the Pulumi stack for the Data Plane.
- Validates that the installation steps complete successfully before exiting.

You can monitor component status in the
[dashboard command](dashboard.md).
