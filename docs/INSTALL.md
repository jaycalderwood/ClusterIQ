# ClusterIQ Installation

## Prerequisites

- .NET 8 SDK
- Hyper-V PowerShell module
- Failover Clusters PowerShell module
- network connectivity to target hosts
- rights to query and administer Hyper-V and cluster resources

## Build

```powershell
dotnet restore
dotnet build -c Release
```

## Publish

```powershell
.\scripts\publish.ps1
```

## First run

Launch ClusterIQ, configure preferences if desired, then connect to a host or cluster.

## Recommended live migration setting

For clustered environments, Kerberos is generally the preferred live migration authentication mode when your environment is configured to support it.
