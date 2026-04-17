# ClusterIQ – Application Documentation

## Overview
ClusterIQ is a WPF-based management console for Hyper-V clusters and Azure Local environments. It combines VM operations, infrastructure visibility, storage insight, and export capability in a single operator-focused interface.

## Core Capabilities

### Virtual Machine Operations
- Start VM
- Stop VM
- Restart VM
- Live Migrate VM

### Infrastructure Visibility
- VM inventory
- VM disk and VM NIC detail
- VM snapshots
- Hyper-V hosts
- Hyper-V cluster
- Hyper-V storage
- Hyper-V switches
- Hyper-V physical NICs
- Azure Stack HCI / S2D
- Health, updates, and performance views

### Export
- Current tab to CSV
- Current tab to XLSX
- Broader dataset export where applicable

## Live Migration
ClusterIQ supports destination-host live migration from the VM inventory view. Migration is asynchronous, inventory refresh follows submission, and UI state can briefly lag environment state.

### Authentication Modes
- Kerberos
- CredSSP

All participating hosts should use the same Hyper-V live migration authentication type.

## Performance View
The HV Perf tab summarizes collected metrics and renders a time-series chart for the selected row. Automatic refresh can be controlled from the refresh interval selector in the performance tab header.

## Settings
ClusterIQ supports:
- saved connection preference
- theme preference
- threshold values
- snapshot retention
- live migration auth preference
- apply-auth-on-connect behavior

## Application Updates
ClusterIQ can silently check GitHub releases at startup and can also perform a manual update check from the About window. When an update is available, the About window can download the latest release asset, apply it to the application directory, and restart the application.

## Data Collection Notes
ClusterIQ relies on live PowerShell collection against hosts and cluster services. Availability of some data depends on module support, permissions, remote connectivity, and cmdlet support on the queried node.

## Operational Notes
- Live migration is asynchronous
- Cluster state may settle after the move before every tab updates
- Storage details depend on the node queried and storage cmdlet support

## Troubleshooting

### Migration succeeded but a tab still showed the old host
Wait briefly and refresh if needed.

### S2D returned a diagnostic row
This indicates storage commands did not return a usable non-primordial pool from the queried node.

### Disk or storage detail is incomplete
Verify permissions, module availability, and storage cmdlet behavior on the queried node.

## Documentation Standard
This documentation reflects the current release version and does not reference internal development phases.
