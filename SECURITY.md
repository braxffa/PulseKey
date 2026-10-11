# Security

PulseKey is a local Windows utility. It has no updater, telemetry, network client, service, or kernel driver. The only browser action is the user-initiated [@BraxFFA](https://linktr.ee/BraxFFA) credit link.

The application uses `SendInput` to create **synthetic** keyboard and mouse events. Scan-code flags do not make these events hardware-originated or undetectable. PulseKey does not bypass anti-cheat, input integrity checks, or application policies.

Macro files are XML data. The loader caps file size at 256 KB, prohibits DTD processing and external XML resolution, and rejects more than 500 steps. Do not open macro files from an untrusted source without inspecting them.

The release executable is currently unsigned. Each release includes SHA-256 hashes so you can verify downloaded assets. A matching hash confirms a file matches the published asset; it does not substitute for malware analysis or code signing.

For a security issue, use GitHub's private vulnerability reporting feature if it is enabled for this repository. If it is unavailable, contact [@BraxFFA](https://linktr.ee/BraxFFA) and avoid publishing exploit details in a public issue.
