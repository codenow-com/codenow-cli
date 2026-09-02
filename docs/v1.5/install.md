# Installation

This page covers installing CodeNOW CLI on supported platforms, validating the
binary, and preparing your environment for cluster operations.

## Table of Contents

- [Supported Platforms](#supported-platforms)
- [Installation Options](#installation-options)
  - [Install from GitHub Releases](#install-from-github-releases)
  - [Offline / Air-gapped Install](#offline--air-gapped-install)
- [Verify the Installation](#verify-the-installation)
- [Next Step](#next-step)

## Supported Platforms

CodeNOW CLI ships as a single static binary for:

- Linux (x64)
- macOS (arm64)
- Windows (x64)

---

## Installation Options

Choose one of the following methods based on your environment:

- **GitHub Releases**: Recommended for most users.
- **Offline / Air-gapped**: For restricted networks.

### Install from GitHub Releases

1. Download the latest release for your platform from
   [GitHub Releases](https://github.com/codenow-com/codenow-cli/releases).
2. Place the `cn` binary on your PATH.

Example:

```bash
sudo mv ./cn /usr/local/bin/cn
```

---

### Offline / Air-gapped Install

1. Download the release artifact from
   [GitHub Releases](https://github.com/codenow-com/codenow-cli/releases) on a machine with internet access.
2. Transfer the binary into the target environment using approved media.
3. Install it on a PATH-visible location.

---

## Verify the Installation

Confirm the CLI is reachable and displays help:

```bash
cn --help
```

Expected outcome: the CLI prints a list of available commands and options.

---

## Next Step

Proceed to the [Quickstart](quickstart.md) to run your first CodeNOW workflow.
