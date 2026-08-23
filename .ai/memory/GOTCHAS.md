# Gotchas

- `az network bastion rdp` / `--rdp` flags are Windows-client-only; macOS/Linux need `az network bastion tunnel` + their own RDP client.
- Bastion native client needs Standard SKU+ with tunneling enabled; Developer/Basic SKUs cannot do CLI connections.
- Entra RDP to Windows requires the client PC Entra-joined/registered to the same tenant as the VM + AADLoginForWindows extension.
- AAD-issued OpenSSH certs are Linux-only today; Windows targets use `--local-user`.
- Arc SSH needs the HybridConnectivity default endpoint + SSH service config (port 22); the CLI prompts to create it (`--yes` to auto-accept).
- Raw Graph `deviceLocalCredentials` returns Base64 — decode before use.
