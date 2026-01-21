# CodeNOW CLI
> A fast, opinionated CLI for managing CodeNOW infrastructure on Kubernetes.

CodeNOW CLI is a cross-platform command-line tool for installing, upgrading, and
operating CodeNOW infrastructure components on Kubernetes clusters. It focuses on predictable, repeatable environments and a smooth operator experience.

Built on .NET 10 with full NativeAOT compatibility, the CLI ships as a single
statically linked executable and starts fast even in minimal container or
air-gapped environments.

## Table of Contents

- [Features](#features)
- [Getting Started](#getting-started)
- [Documentation](#documentation)
- [Development](#development)
- [Roadmap](#roadmap)
- [Contributing](#contributing)
- [License](#license)

## Features

- Kubernetes-native provisioning and operational workflows
- Single statically linked binary with fast startup times
- Interactive guided setup for first-time installations
- Clear diagnostics and operational visibility
- Configuration-driven behavior suitable for GitOps

## Getting Started

### Prerequisites

- Access to a Kubernetes cluster
- Sufficient permissions to install or manage CodeNOW components

### Installation

Download the latest release for your platform from
[GitHub Releases](https://github.com/codenow-com/codenow-cli/releases) and place
the `cn` binary on your PATH.

### Usage

```bash
cn --help
```

## Documentation

The complete user guide is available in the
[latest documentation](https://codenow-com.github.io/codenow-cli/docs/latest),
with versioned snapshots for each release.

## Development

- `make restore` restores NuGet dependencies for the main project and tests.
- `make build` builds the CLI project.
- `make test` runs the test suite.
- `make format` formats the solution using `dotnet format`.
- `make clean` cleans build outputs for the main project and tests.
- `make run -- <args>` runs the CLI with arguments passed after `--`.
- `make update-pulumi-operator` downloads and refreshes Pulumi operator manifests and metadata under `assets/`.

## Roadmap

See the [open issues](https://github.com/codenow-com/codenow-cli/issues) for planned work.

## Contributing

Issues and pull requests are welcome. Please include a clear description, steps
to reproduce, and expected behavior when reporting bugs.

## License

Apache License 2.0. See LICENSE and NOTICE.
