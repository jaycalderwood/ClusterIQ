# ClusterIQ User Guide

## Connect to an environment

Enter a host or cluster name, choose your authentication approach, and connect. If Azure-connected collection is enabled in your environment, provide the required subscription and optional tenant or resource group values.

## Navigate the interface

ClusterIQ organizes data by tab. The active tab controls current-tab export behavior.

Main tabs include:

- VM inventory
- VM disk
- VM NIC
- VM snapshot
- Hyper-V host
- Hyper-V cluster
- Hyper-V storage
- Hyper-V switch
- Hyper-V NIC
- Azure Stack HCI / S2D
- Health
- Updates
- Performance

## Run VM actions

Select a VM in the VM inventory tab to enable:

- Start
- Stop
- Restart
- Live Migrate

### Live migration workflow

1. Select a VM
2. Select a destination host from the host dropdown
3. Click Live Migrate
4. ClusterIQ submits the move and refreshes inventory
5. The status bar reports whether the VM appears on the destination host after refresh

If the cluster has not settled yet, use Refresh again.

## Settings

ClusterIQ settings include:

- saved connection preference
- dark mode
- notification and update preferences
- thresholds
- live migration auth preference
- apply-auth-on-connect behavior

### Live migration auth preference

Supported options:

- Kerberos
- CredSSP

Use Kerberos for clustered live migration when your environment is configured for it. Use CredSSP only when that matches your operational model.

## Export

Use the current-tab export commands to export only the visible tab to:

- CSV
- XLSX

Use broader export commands when you want full output across tabs.

## Troubleshooting

### Live migration worked but the VM row still showed the old host
Use Refresh if the cluster has not yet fully reflected the move. ClusterIQ also refreshes automatically after migration, but state can lag briefly in the environment.

### S2D tab shows a diagnostic row
A diagnostic row means the storage commands did not return a usable non-primordial pool from the queried node. Review module availability, permissions, and storage cmdlet behavior on that node.

### Migration works one way but not the other
Verify that all participating hosts use the same Hyper-V live migration authentication type and that the chosen auth model is supported by your environment.
