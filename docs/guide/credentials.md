# Credentials and authentication

Stagecoach separates two kinds of identity:

- **Entra identity**: authenticates Azure control-plane discovery and connection establishment. Its Windows-encrypted Azure CLI token cache is isolated per Stagecoach profile.
- **Connection identity**: authenticates inside the destination or to the Arc SSH service. Its password, when saved, is stored as a generic Windows Credential Manager credential.

Stagecoach never asks for an Entra password and does not copy target passwords into SQLCipher metadata, settings, logs, command lines, or `.rdp` files. For RDP it creates a temporary `TERMSRV/<endpoint>` credential with session persistence and deletes it when Stagecoach observes session completion or the operator stops the session.

For password-based Arc SSH, `SSH_ASKPASS` points to `Stagecoach.AskPass.exe`. The helper receives only a profile identifier, reads the corresponding Windows credential, writes the password to the requesting process, and exits. The password is not placed in an environment variable or command-line argument.

Windows Credential Manager and DPAPI bind saved data to the current Windows security context. This works in normal console and RDP sessions when the same user profile is loaded. Copying the data directory to another Windows account does not make the encrypted data usable.
