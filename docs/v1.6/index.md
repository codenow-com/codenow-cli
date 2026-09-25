# CodeNOW CLI Documentation

Welcome to the complete user documentation for CodeNOW CLI. This documentation
targets platform operators who want a reliable, repeatable way to install and
manage CodeNOW infrastructure on Kubernetes.

## Table of Contents

- [What You'll Find Here](#what-youll-find-here)
- [Get Started](#get-started)
- [Reference](#reference)
- [Conventions Used](#conventions-used)

## What You'll Find Here

- Clear installation paths for online and air-gapped environments, including
  prerequisites, required access, and where to place offline artifacts so the
  CLI can run without external dependencies.
- A quickstart that validates cluster access, confirms your kubeconfig context,
  and walks through the first CLI command end to end.
- Command and configuration references with practical guidance, including
  common flags, expected outputs, and pointers on how to troubleshoot failures.

## Get Started

- [Install the CLI](install.md)
- [Quickstart](quickstart.md)

---

## Reference

- [Commands](commands/index.md)

---

## Conventions Used

- Commands are shown in `bash` blocks and can be copied directly.
- Replace placeholders in `<angle-brackets>` with your values.
- The term "cluster" refers to a Kubernetes cluster reachable via `kubeconfig`.
