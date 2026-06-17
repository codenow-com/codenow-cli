# Dashboard Command

Opens an interactive, real-time dashboard for the CodeNOW Data Plane. The
dashboard highlights cluster version and capacity, operator and stack status,
and live logs from the operator and workspace pods so you can monitor progress
and diagnose issues without jumping between multiple tools.

## Table of Contents

- [Prerequisites](#prerequisites)
- [Usage](#usage)
- [What It Displays](#what-it-displays)
  - [Status Panel](#status-panel)
  - [Action Panel](#action-panel)
  - [Log Panel](#log-panel)
- [Common Workflow](#common-workflow)

## Prerequisites

The dashboard connects to the Data Plane namespaces and watches operator and
stack resources in the cluster. Make sure the Data Plane is bootstrapped.

If the Data Plane is not installed yet, complete the bootstrap flow first
using the [bootstrap command](bootstrap.md).

## Usage

Run the command with:

```bash
cn dp dashboard
```

## What It Displays

The dashboard is split into three panels and adapts to terminal size. The view
refreshes automatically every second.

### Status Panel

The status panel provides a quick status summary:

- **Cluster**: Kubernetes control plane version and requested capacity usage (CPU/memory).
- **Data Plane Operator**: Operator version, namespace, and reconciliation status.
- **Data Plane Stack**: Stack readiness and whether reconciliation is paused or running.

Use this section to confirm that the operator is healthy and that stack updates
are progressing as expected.

### Action Panel

The action panel highlights available commands and the most recent action
state. Use the following keyboard shortcuts:

- `R` request a reconcile for the Data Plane stack
- `D` toggle dry-run mode on the stack (plan-only reconcile).
- `O` switch to operator logs
- `W` switch to workspace logs
- `N` load fresh logs after a pod restart
- `↑` / `↓` scroll log pages
- `Q` quit the dashboard

> Tip: Press `D` to switch into preview mode, which triggers a reconcile and
> shows the planned changes. If the preview looks good, press `D` again to
> switch dry-run off, then run the reconcile to apply the update.

### Log Panel

The log panel is a live log stream from one of the Data Plane pods:

- **Operator logs** show control-plane reconciliation events, resource
  validation, and errors emitted by the operator while it processes the Data
  Plane stack definition.
- **Workspace logs** show the Pulumi runtime output for the current stack
  action, including plan/apply steps, resource previews, and any deployment
  failures returned by Pulumi.

Switch between the two streams with the keyboard shortcuts in the action panel.

Additional log handling details:

- The dashboard refreshes every second and reflows with terminal resizing.
- Logs are fetched in batches (tailing up to 1000 lines) to avoid flooding the
  UI with very large history.
- If a pod restarts, the dashboard keeps the previous logs and prompts you to
  press `N` to switch to the new pod logs.
- When logs are unavailable, the dashboard displays a placeholder message to
  indicate that the stream is currently empty.

## Common Workflow

A typical monitoring loop looks like this:

1. Run `cn dp dashboard` and check the top panel for operator/stack status.
2. Switch to workspace logs (`W`) if a reconcile is running to follow progress.
3. Trigger a reconcile (`R`) after adjusting configuration or toggling dry-run.
4. Use operator logs (`O`) to diagnose errors or reconciliation failures.
5. Quit the dashboard (`Q`) once the stack is stable and resources are healthy.
