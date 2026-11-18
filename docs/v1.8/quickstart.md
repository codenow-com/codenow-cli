# Quickstart

This quickstart explains how to authenticate and connect the CLI to a cluster.

> Note: The CLI does not require additional installed dependencies such as
> `kubectl`.

## Table of Contents

- [Select a Connection Method](#select-a-connection-method)
  - [Authenticate With KUBECONFIG](#authenticate-with-kubeconfig)
  - [Authenticate With Kube Proxy URL](#authenticate-with-kube-proxy-url)
- [Next Steps](#next-steps)

## Select a Connection Method

Select the connection method that fits your setup:

- Set `KUBECONFIG` to point at your kubeconfig.
- Provide a proxy endpoint with the global `--kube-proxy-url` parameter.

### Authenticate With KUBECONFIG

Use this method when your kubeconfig provides direct access to the cluster API.
Set the environment variable and run the CLI normally:

```bash
export KUBECONFIG=/path/to/kubeconfig
cn <command>
```

If your kubeconfig requires
[Exec plugin authentication](https://kubernetes.io/docs/reference/access-authn-authz/authentication/#client-go-credential-plugins),
this method may not be available in your environment because it can require
interactive user authentication. Use the Kube Proxy URL option instead.

### Authenticate With Kube Proxy URL

Use this method when your kubeconfig relies on an Exec plugin or when you want
to connect through a `kubectl proxy` endpoint:

```bash
cn <command> --kube-proxy-url http://127.0.0.1:8001
```

The default value is `http://127.0.0.1:8001`. You can run `kubectl proxy` on a
workstation where `kubectl` is installed and already authenticated, then
forward the port over an SSH tunnel if needed.

---

## Next Steps

- Explore the [Commands](commands/index.md) reference.
