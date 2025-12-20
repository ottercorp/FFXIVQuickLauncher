# .NET 10.0 Upgrade Plan

## Execution Steps

Execute steps below sequentially one by one in the order they are listed.

1. Validate that an .NET 10.0 SDK required for this upgrade is installed on the machine and if not, help to get it installed.
2. Ensure that the SDK version specified in global.json files is compatible with the .NET 10.0 upgrade.
3. Upgrade FfxivArgLauncher\FfxivArgLauncher.csproj
4. Upgrade XIVLauncher.Common\XIVLauncher.Common.csproj
5. Upgrade XIVLauncher.Common.Windows\XIVLauncher.Common.Windows.csproj
6. Upgrade XIVLauncher.PatchInstaller\XIVLauncher.PatchInstaller.csproj
7. Upgrade XIVLauncher.ArgReader\XIVLauncher.ArgReader.csproj
8. Upgrade XIVLauncher.Common.Unix\XIVLauncher.Common.Unix.csproj
9. Upgrade XIVLauncher.Common.Tests\XIVLauncher.Common.Tests.csproj
10. Upgrade XIVLauncher\XIVLauncher.csproj

## Settings

This section contains settings and data used by execution steps.

### Excluded projects

Table below contains projects that do belong to the dependency graph for selected projects and should not be included in the upgrade.

| Project name                                   | Description                 |
|:-----------------------------------------------|:---------------------------:|
| None                                           | All projects included       |

### Aggregate NuGet packages modifications across all projects

NuGet packages used across all selected projects or their dependencies that need version update in projects that reference them.

| Package Name                        | Current Version         | New Version | Description                                              |
|:------------------------------------|:-----------------------:|:-----------:|:---------------------------------------------------------|
| AdysTech.CredentialManager          | 1.8.0                   | 2.6.0       | Incompatible with .NET 10.0; update to supported version.|
| Extended.Wpf.Toolkit                | 3.5.0                   | 5.0.0       | Incompatible with .NET 10.0; update to supported version.|
| Microsoft.CSharp                    | 4.5.0                   | 4.7.0       | Recommended version for .NET 10.0 compatibility.        |
| Microsoft.Win32.Registry            | 6.0.0-preview.5.21301.5 |             | Functionality included with framework; remove package.   |
| Newtonsoft.Json                     | 13.0.3                  | 13.0.4      | Recommended update for .NET 10.0.                        |
| PInvoke.Kernel32                    | 0.7.124                 | 0.7.124     | Package deprecated; update to latest available version.  |
| System.Collections.Immutable        | 1.5.0                   | 10.0.1      | Recommended update for .NET 10.0.                        |
| System.Drawing.Common               | 8.0.10                  | 10.0.1      | Recommended update for .NET 10.0.                        |
| System.Management                   | 6.0.0                   | 10.0.1      | Recommended update for .NET 10.0.                        |
| System.Memory                       | 4.5.5                   |             | Functionality included with framework; remove package.   |
| System.Security.Principal.Windows   | 5.0.0                   |             | Functionality included with framework; remove package.   |
| System.Text.Encodings.Web           | 8.0.0                   | 10.0.1      | Recommended update for .NET 10.0.                        |
| System.Text.Json                    | 8.0.5                   | 10.0.1      | Recommended update for .NET 10.0.                        |
| System.Threading.Channels           | 8.0.0                   | 10.0.1      | Recommended update for .NET 10.0.                        |

### Project upgrade details
This section contains details about each project upgrade and modifications that need to be done in the project.

#### FfxivArgLauncher\FfxivArgLauncher.csproj modifications

Project properties changes:
  - Target framework should be changed from `net8.0` to `net10.0`

Other changes:
  - None identified beyond the target framework update.

#### XIVLauncher.Common\XIVLauncher.Common.csproj modifications

Project properties changes:
  - Target framework should be changed from `net8.0` to `net10.0`

NuGet packages changes:
  - Newtonsoft.Json should be updated from `13.0.3` to `13.0.4` (*recommended for .NET 10.0*)
  - System.Management should be updated from `6.0.0` to `10.0.1` (*recommended for .NET 10.0*)
  - System.Text.Json should be updated from `8.0.5` to `10.0.1` (*recommended for .NET 10.0*)
  - System.Memory should be removed (*functionality included with framework*)
  - System.Security.Principal.Windows should be removed (*functionality included with framework*)

Other changes:
  - None identified beyond target framework and package updates.

#### XIVLauncher.Common.Windows\XIVLauncher.Common.Windows.csproj modifications

Project properties changes:
  - Target framework should be changed from `net8.0-windows` to `net10.0-windows`

NuGet packages changes:
  - Microsoft.Win32.Registry should be removed (*functionality included with framework*)
  - PInvoke.Kernel32 should be updated from `0.7.124` to `0.7.124` (*package deprecated; update to latest available version*)

Other changes:
  - None identified beyond target framework and package updates.

#### XIVLauncher.PatchInstaller\XIVLauncher.PatchInstaller.csproj modifications

Project properties changes:
  - Target framework should be changed from `net8.0-windows` to `net10.0-windows`

NuGet packages changes:
  - Newtonsoft.Json should be updated from `13.0.3` to `13.0.4` (*recommended for .NET 10.0*)
  - System.Threading.Channels should be updated from `8.0.0` to `10.0.1` (*recommended for .NET 10.0*)

Other changes:
  - None identified beyond target framework and package updates.

#### XIVLauncher.ArgReader\XIVLauncher.ArgReader.csproj modifications

Project properties changes:
  - Target framework should be changed from `net8.0` to `net10.0`

NuGet packages changes:
  - Newtonsoft.Json should be updated from `13.0.3` to `13.0.4` (*recommended for .NET 10.0*)
  - System.Threading.Channels should be updated from `8.0.0` to `10.0.1` (*recommended for .NET 10.0*)

Other changes:
  - None identified beyond target framework and package updates.

#### XIVLauncher.Common.Unix\XIVLauncher.Common.Unix.csproj modifications

Project properties changes:
  - Target framework should be changed from `net8.0` to `net10.0`

Other changes:
  - None identified beyond the target framework update.

#### XIVLauncher.Common.Tests\XIVLauncher.Common.Tests.csproj modifications

Project properties changes:
  - Target framework should be changed from `net8.0` to `net10.0`

Other changes:
  - None identified beyond the target framework update.

#### XIVLauncher\XIVLauncher.csproj modifications

Project properties changes:
  - Target framework should be changed from `net8.0-windows10.0.22000.0` to `net10.0-windows`

NuGet packages changes:
  - AdysTech.CredentialManager should be updated from `1.8.0` to `2.6.0` (*incompatible with .NET 10.0*)
  - Extended.Wpf.Toolkit should be updated from `3.5.0` to `5.0.0` (*incompatible with .NET 10.0*)
  - Microsoft.CSharp should be updated from `4.5.0` to `4.7.0` (*recommended for .NET 10.0*)
  - System.Collections.Immutable should be updated from `1.5.0` to `10.0.1` (*recommended for .NET 10.0*)
  - System.Drawing.Common should be updated from `8.0.10` to `10.0.1` (*recommended for .NET 10.0*)
  - System.Text.Encodings.Web should be updated from `8.0.0` to `10.0.1` (*recommended for .NET 10.0*)

Other changes:
  - None identified beyond target framework and package updates.
