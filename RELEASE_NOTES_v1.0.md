# ClusterIQ v1.0 Release Notes

## Highlights

- Stable VM action workflow
- Working live migration with destination host selection
- Current-tab export for CSV and XLSX
- Hyper-V, cluster, switch, NIC, and S2D inventory views
- Saved preferences and environment profiles
- Live migration auth preference setting with Kerberos / CredSSP options

## Notes

- Live migration is asynchronous
- Inventory refresh is automatic after migration but environment state may still settle briefly
- Storage data depends on the storage cmdlets supported by the queried node
