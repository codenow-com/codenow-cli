# Config Command

Encrypts secret values in a plaintext Data Plane operator configuration file
and overwrites the file in place. The generated encryption key is printed to
stdout for automation scenarios.

## Table of Contents

- [Usage](#usage)
- [What It Does](#what-it-does)
- [Output](#output)
- [Notes](#notes)

## Usage

Run the command with:

```bash
cn dp config encrypt --config cn-data-plane-operator.json
```

## What It Does

The command:

- Loads the provided JSON configuration file.
- Encrypts all secret fields (for example, registry passwords, tokens, and
  passphrases).
- Rewrites the same JSON file with encrypted values.

## Output

The command prints the generated encryption key to stdout. Use this key to
decrypt the configuration later (for example, when running `cn dp bootstrap
--config ...`).

## Notes

- The command overwrites the input file. Keep a backup if you need the
  plaintext version.
- Store the encryption key securely. Without it, the configuration cannot be
  decrypted.
