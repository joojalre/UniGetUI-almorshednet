# Maintained DataGrid provenance and notices

Upstream: AvaloniaUI/Avalonia.Controls.DataGrid, tag 12.0.0, commit 3d2cf024d6f15d4e770f655246185ad417f93f51.
The exact archive URL/hash, file hashes and original project metadata are recorded in provenance/.
All existing source copyright and Microsoft Public License headers are retained.
Original repository MIT text: provenance/licence.md. Ms-PL text: provenance/Microsoft-Public-License.txt,
reproduced from the Microsoft-maintained notice source recorded in additional-license-source.json.
This retention does not assert relicensing of inherited source files or legal clearance.

Local changes add explicit item metadata/factories and exclude legacy reflection during C# compilation.
They retain the original control/collection engine and theme paths. This is a locally maintained variant,
not an official Avalonia release. Legacy source bodies remain under a compile symbol rejected both by
MSBuild and a source #error; they are retained for source comparison, not a supported runtime mode.

Assembly identity is retained using DelaySign and only the public key published in the pinned upstream
Directory.Build.props. No upstream private .snk file was extracted or used. The copied assembly is not
cryptographically signed by Avalonia. Microsoft documents DelaySign as requiring only the public key:
https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-options/security
